using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers.Dynamic;
using SPTarkov.Server.Core.Routers.Static;
using SPTarkov.Server.Core.Utils;

namespace PmcSpawnToggle;

public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.bensburnedwaffles.pmcspawntoggle";
    public string Name { get; init; } = "PMC Spawn Toggle";
    public string Author { get; init; } = "BensBurnedWaffles";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.2");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 2000)]
public sealed class PmcSpawnToggleMod : IOnLoad
{
    private const string ConfigFileName = "com.bensburnedwaffles.pmcspawntoggle.cfg";
    private const string DisableSettingName = "Disable PMC Spawns";
    private const string UsecRole = "pmcUSEC";
    private const string BearRole = "pmcBEAR";

    private readonly LocationTable _locations;
    private readonly PmcConfig _pmcConfig;
    private readonly ISptLogger<PmcSpawnToggleMod> _logger;
    private readonly Dictionary<string, List<RemovedBossWave>> _removedBossWaves =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RemovedNormalWave>> _removedNormalWaves =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RemovedBossWave>> _removedConfiguredPmcWaves =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _modeLock = new();

    private string _configPath = string.Empty;
    private bool _disablePmcs;

    public PmcSpawnToggleMod(
        LocationTable locations,
        PmcConfig pmcConfig,
        ISptLogger<PmcSpawnToggleMod> logger)
    {
        _locations = locations;
        _pmcConfig = pmcConfig;
        _logger = logger;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _configPath = GetConfigPath();
        _disablePmcs = ReadDisableSetting();
        int changedWaves = ApplyMode(_disablePmcs);

        _logger.Success(
            _disablePmcs
                ? $"PMC Spawn Toggle loaded with PMCs disabled; removed {changedWaves} PMC wave(s)."
                : "PMC Spawn Toggle loaded with normal PMC spawning enabled.",
            null);

        return Task.CompletedTask;
    }

    internal bool RefreshModeFromConfig()
    {
        bool requestedMode = ReadDisableSetting();
        SetMode(requestedMode);
        return true;
    }

    internal void SetMode(bool requestedMode)
    {
        lock (_modeLock)
        {
            if (requestedMode != _disablePmcs)
            {
                _disablePmcs = requestedMode;
                int changedWaves = ApplyMode(_disablePmcs);
                _logger.Info(
                    _disablePmcs
                        ? $"PMC spawning disabled; removed {changedWaves} PMC wave(s)."
                        : $"PMC spawning enabled; restored {changedWaves} PMC wave(s).",
                    null);
            }
            else if (_disablePmcs)
            {
                // Catch waves added after this mod's load stage by SPT or another server mod.
                RemovePmcWaves();
            }
        }
    }

    private int ApplyMode(bool disablePmcs)
    {
        return disablePmcs ? RemovePmcWaves() : RestorePmcWaves();
    }

    private int RemovePmcWaves()
    {
        int removedCount = 0;

        foreach ((string locationId, List<BossLocationSpawn> configuredWaves) in _pmcConfig.CustomPmcWaves)
        {
            for (int index = configuredWaves.Count - 1; index >= 0; index--)
            {
                BossLocationSpawn wave = configuredWaves[index];
                if (!IsPmcRole(wave.BossName))
                {
                    continue;
                }

                GetStoredConfiguredPmcWaves(locationId).Add(new RemovedBossWave(index, wave));
                configuredWaves.RemoveAt(index);
                removedCount++;
            }
        }

        foreach ((string locationId, Location locationData) in _locations.GetDictionary())
        {
            LocationBase? location = locationData?.Base;
            List<BossLocationSpawn>? bossWaves = location?.BossLocationSpawn;
            if (bossWaves is not null)
            {
                for (int index = bossWaves.Count - 1; index >= 0; index--)
                {
                    BossLocationSpawn wave = bossWaves[index];
                    if (!IsPmcRole(wave.BossName))
                    {
                        continue;
                    }

                    GetStoredBossWaves(locationId).Add(new RemovedBossWave(index, wave));
                    bossWaves.RemoveAt(index);
                    removedCount++;
                }
            }

            List<Wave>? normalWaves = location?.Waves;
            if (normalWaves is null)
            {
                continue;
            }

            for (int index = normalWaves.Count - 1; index >= 0; index--)
            {
                Wave wave = normalWaves[index];
                if (wave.WildSpawnType != WildSpawnType.pmcUSEC &&
                    wave.WildSpawnType != WildSpawnType.pmcBEAR)
                {
                    continue;
                }

                GetStoredNormalWaves(locationId).Add(new RemovedNormalWave(index, wave));
                normalWaves.RemoveAt(index);
                removedCount++;
            }
        }

        return removedCount;
    }

    private int RestorePmcWaves()
    {
        int restoredCount = 0;

        foreach ((string locationId, List<RemovedBossWave> storedWaves) in _removedConfiguredPmcWaves)
        {
            if (!_pmcConfig.CustomPmcWaves.TryGetValue(
                    locationId,
                    out List<BossLocationSpawn>? configuredWaves))
            {
                configuredWaves = new List<BossLocationSpawn>();
                _pmcConfig.CustomPmcWaves[locationId] = configuredWaves;
            }

            foreach (RemovedBossWave stored in storedWaves.OrderBy(item => item.Index))
            {
                if (configuredWaves.Contains(stored.Wave))
                {
                    continue;
                }

                int insertAt = Math.Min(stored.Index, configuredWaves.Count);
                configuredWaves.Insert(insertAt, stored.Wave);
                restoredCount++;
            }
        }

        Dictionary<string, Location> locations = _locations.GetDictionary();

        foreach ((string locationId, List<RemovedBossWave> storedWaves) in _removedBossWaves)
        {
            if (!locations.TryGetValue(locationId, out Location? locationData))
            {
                continue;
            }

            LocationBase? location = locationData?.Base;
            List<BossLocationSpawn>? bossWaves = location?.BossLocationSpawn;
            if (bossWaves is null)
            {
                continue;
            }

            foreach (RemovedBossWave stored in storedWaves.OrderBy(item => item.Index))
            {
                if (bossWaves.Contains(stored.Wave))
                {
                    continue;
                }

                int insertAt = Math.Min(stored.Index, bossWaves.Count);
                bossWaves.Insert(insertAt, stored.Wave);
                restoredCount++;
            }
        }

        foreach ((string locationId, List<RemovedNormalWave> storedWaves) in _removedNormalWaves)
        {
            if (!locations.TryGetValue(locationId, out Location? locationData))
            {
                continue;
            }

            LocationBase? location = locationData?.Base;
            List<Wave>? normalWaves = location?.Waves;
            if (normalWaves is null)
            {
                continue;
            }

            foreach (RemovedNormalWave stored in storedWaves.OrderBy(item => item.Index))
            {
                if (normalWaves.Contains(stored.Wave))
                {
                    continue;
                }

                int insertAt = Math.Min(stored.Index, normalWaves.Count);
                normalWaves.Insert(insertAt, stored.Wave);
                restoredCount++;
            }
        }

        _removedBossWaves.Clear();
        _removedNormalWaves.Clear();
        _removedConfiguredPmcWaves.Clear();
        return restoredCount;
    }

    private List<RemovedBossWave> GetStoredBossWaves(string locationId)
    {
        if (!_removedBossWaves.TryGetValue(locationId, out List<RemovedBossWave>? waves))
        {
            waves = new List<RemovedBossWave>();
            _removedBossWaves[locationId] = waves;
        }

        return waves;
    }

    private List<RemovedNormalWave> GetStoredNormalWaves(string locationId)
    {
        if (!_removedNormalWaves.TryGetValue(locationId, out List<RemovedNormalWave>? waves))
        {
            waves = new List<RemovedNormalWave>();
            _removedNormalWaves[locationId] = waves;
        }

        return waves;
    }

    private List<RemovedBossWave> GetStoredConfiguredPmcWaves(string locationId)
    {
        if (!_removedConfiguredPmcWaves.TryGetValue(locationId, out List<RemovedBossWave>? waves))
        {
            waves = new List<RemovedBossWave>();
            _removedConfiguredPmcWaves[locationId] = waves;
        }

        return waves;
    }

    private static bool IsPmcRole(string? role)
    {
        return role?.Equals(UsecRole, StringComparison.OrdinalIgnoreCase) == true ||
               role?.Equals(BearRole, StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool ReadDisableSetting()
    {
        if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
        {
            return false;
        }

        try
        {
            foreach (string line in File.ReadLines(_configPath))
            {
                string trimmed = line.Trim();
                int separator = trimmed.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                string key = trimmed[..separator].Trim();
                if (!key.Equals(DisableSettingName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return bool.TryParse(trimmed[(separator + 1)..].Trim(), out bool enabled) && enabled;
            }
        }
        catch (IOException)
        {
            // BepInEx can briefly hold the file while it saves the new value.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the last valid mode if the file is temporarily unavailable.
        }

        return _disablePmcs;
    }

    private static string GetConfigPath()
    {
        DirectoryInfo runtimeDirectory = new(AppContext.BaseDirectory);
        DirectoryInfo? sptRoot = runtimeDirectory.Name.Equals(
            "SPT_Runtime",
            StringComparison.OrdinalIgnoreCase)
            ? runtimeDirectory.Parent
            : runtimeDirectory;

        return Path.Combine(
            sptRoot?.FullName ?? AppContext.BaseDirectory,
            "BepInEx",
            "config",
            ConfigFileName);
    }

    private sealed record RemovedBossWave(int Index, BossLocationSpawn Wave);
    private sealed record RemovedNormalWave(int Index, Wave Wave);
}

public sealed record PmcSpawnModeRequest : IRequestData
{
    public bool DisablePmcs { get; init; }
}

[Injectable]
public sealed class PmcSpawnToggleRouter : StaticRouter
{
    public const string SyncRoute = "/bensburnedwaffles/pmc-spawn-toggle/sync";

    public PmcSpawnToggleRouter(JsonUtil jsonUtil, PmcSpawnToggleMod mod)
        : base(
            jsonUtil,
            new RouteAction[]
            {
                new RouteAction<PmcSpawnModeRequest>(
                    SyncRoute,
                    (url, request, sessionId, output, cancellationToken) =>
                        HandleSyncAsync(mod, request, cancellationToken))
            })
    {
    }

    private static ValueTask<string> HandleSyncAsync(
        PmcSpawnToggleMod mod,
        PmcSpawnModeRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        mod.SetMode(request.DisablePmcs);
        return ValueTask.FromResult("{\"success\":true}");
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 2001)]
public sealed class PmcRaidRequestRefresh : IOnLoad
{
    private readonly PmcSpawnToggleMod _mod;
    private readonly MatchStaticRouter _matchRouter;
    private readonly InraidDynamicRouter _inraidRouter;

    public PmcRaidRequestRefresh(
        PmcSpawnToggleMod mod,
        MatchStaticRouter matchRouter,
        InraidDynamicRouter inraidRouter)
    {
        _mod = mod;
        _matchRouter = matchRouter;
        _inraidRouter = inraidRouter;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _matchRouter.OnBeforeAction += RefreshBeforeRaidRequest;
        _inraidRouter.OnBeforeAction += RefreshBeforeRaidRequest;
        return Task.CompletedTask;
    }

    private void RefreshBeforeRaidRequest(object? sender, IOnBeforeEventRequestData requestData)
    {
        _mod.RefreshModeFromConfig();
    }
}

[Injectable(TypePriority = OnUpdateOrder.BtrDeliveryCallbacks + 2000)]
public sealed class PmcSpawnModeWatcher : IOnUpdate
{
    private readonly PmcSpawnToggleMod _mod;

    public PmcSpawnModeWatcher(PmcSpawnToggleMod mod)
    {
        _mod = mod;
    }

    public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_mod.RefreshModeFromConfig());
    }
}
