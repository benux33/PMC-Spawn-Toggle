using System;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using SPT.Common.Http;
using UnityEngine;

namespace PmcSpawnToggle.Client
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.bensburnedwaffles.pmcspawntoggle";
        public const string PluginName = "PMC Spawn Toggle";
        public const string PluginVersion = "1.0.1";

        private const string SettingSection = "PMC Spawning";
        private const string SettingName = "Disable PMC Spawns";
        private const string SyncRoute = "/bensburnedwaffles/pmc-spawn-toggle/sync";
        private const float SyncRetrySeconds = 2f;

        private static ConfigEntry<bool> _disablePmcSpawns;
        private static bool _raidActive;
        private static bool _lockedValue;
        private static bool _restoringLockedValue;
        private bool _hasSynced;
        private bool _lastSyncedValue;
        private float _nextSyncAttempt;
        private bool _loggedSyncFailure;

        private void Awake()
        {
            ConfigurationManagerAttributes menuAttributes = new ConfigurationManagerAttributes
            {
                CustomDrawer = DrawSetting,
                HideDefaultButton = true,
                Order = 100
            };

            _disablePmcSpawns = Config.Bind(
                SettingSection,
                SettingName,
                false,
                new ConfigDescription(
                    "Off: SPT spawns PMCs normally. On: BEAR and USEC AI waves are removed while " +
                    "Scavs, bosses, Rogues, Raiders, and followers remain. The setting locks when a raid begins.",
                    null,
                    menuAttributes));

            _lockedValue = _disablePmcSpawns.Value;
            _disablePmcSpawns.SettingChanged += OnSettingChanged;
            Config.Save();
            TrySyncWithServer();

            Logger.LogInfo("PMC Spawn Toggle loaded. The F12 choice is editable only outside raids.");
        }

        private void Update()
        {
            bool currentlyInRaid = IsRaidActiveNow();
            if (currentlyInRaid && !_raidActive)
            {
                _lockedValue = _disablePmcSpawns.Value;
            }
            else if (!currentlyInRaid)
            {
                _lockedValue = _disablePmcSpawns.Value;
            }

            _raidActive = currentlyInRaid;

            if (!_raidActive &&
                (!_hasSynced || _lastSyncedValue != _disablePmcSpawns.Value) &&
                Time.unscaledTime >= _nextSyncAttempt)
            {
                TrySyncWithServer();
            }

            if (_raidActive && _disablePmcSpawns.Value != _lockedValue)
            {
                RestoreLockedValue();
            }
        }

        private void OnDestroy()
        {
            if (_disablePmcSpawns != null)
            {
                _disablePmcSpawns.SettingChanged -= OnSettingChanged;
            }
        }

        private void OnSettingChanged(object sender, EventArgs eventArgs)
        {
            if (_restoringLockedValue)
            {
                return;
            }

            if (IsRaidActiveNow())
            {
                RestoreLockedValue();
                return;
            }

            _lockedValue = _disablePmcSpawns.Value;
            _disablePmcSpawns.ConfigFile.Save();
            _hasSynced = false;
            TrySyncWithServer();
        }

        private void TrySyncWithServer()
        {
            bool requestedMode = _disablePmcSpawns.Value;
            _nextSyncAttempt = Time.unscaledTime + SyncRetrySeconds;

            try
            {
                RequestHandler.PostJson(
                    SyncRoute,
                    requestedMode
                        ? "{\"DisablePmcs\":true}"
                        : "{\"DisablePmcs\":false}");

                _lastSyncedValue = requestedMode;
                _hasSynced = true;
                _loggedSyncFailure = false;
            }
            catch (Exception exception)
            {
                _hasSynced = false;
                if (!_loggedSyncFailure)
                {
                    Logger.LogWarning(
                        "Could not sync the PMC setting yet; it will retry before the raid. " +
                        exception.Message);
                    _loggedSyncFailure = true;
                }
            }
        }

        private static void RestoreLockedValue()
        {
            if (_disablePmcSpawns == null)
            {
                return;
            }

            _restoringLockedValue = true;
            try
            {
                _disablePmcSpawns.Value = _lockedValue;
                _disablePmcSpawns.ConfigFile.Save();
            }
            finally
            {
                _restoringLockedValue = false;
            }
        }

        private static void DrawSetting(ConfigEntryBase setting)
        {
            bool inRaid = IsRaidActiveNow();
            bool previousGuiState = GUI.enabled;
            GUI.enabled = previousGuiState && !inRaid;

            bool currentValue = (bool)setting.BoxedValue;
            bool selectedValue = GUILayout.Toggle(
                currentValue,
                inRaid ? "Locked until the raid ends" : string.Empty);

            if (!inRaid && selectedValue != currentValue)
            {
                setting.BoxedValue = selectedValue;
            }

            GUI.enabled = previousGuiState;
        }

        private static bool IsRaidActiveNow()
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                return false;
            }

            GameWorld world = Singleton<GameWorld>.Instance;
            return world != null &&
                   world.MainPlayer != null &&
                   !string.IsNullOrEmpty(world.LocationId) &&
                   !world.LocationId.Equals("hideout", StringComparison.OrdinalIgnoreCase);
        }
    }
}
