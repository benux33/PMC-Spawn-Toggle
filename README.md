# PMC Spawn Toggle

PMC Spawn Toggle adds a pre-raid F12 option for turning SPT's AI PMC spawns on
or off without editing the base server database.

## F12 option

`Disable PMC Spawns`

- **Off (default):** SPT spawns BEAR and USEC AI normally.
- **On:** BEAR and USEC AI waves are removed from every location.

The option becomes greyed out and is programmatically locked after a raid
starts. It becomes editable again after returning to the main menu.

Version 1.0.1 sends the choice directly to the SPT server as soon as it changes.
The server also reads it again during the pre-raid configuration and location
loot requests, preventing a raid from starting with a stale F12 value.

## What remains when PMCs are disabled

- Ordinary and sniper Scavs
- Bosses and their normal followers
- Rogues
- Raiders (`pmcBot` is Tarkov's internal Raider role)
- Cultists and other special AI

Only the real AI PMC roles, `pmcUSEC` and `pmcBEAR`, are removed. Your own PMC
character is unaffected.

## Installation

1. Close Escape from Tarkov and the SPT server.
2. Open the release ZIP.
3. Copy the `BepInEx` and `SPT_Runtime` folders into the root of the SPT
   installation.
4. Allow Windows to merge the folders.
5. Start the server and game, open F12 in the main menu, and choose the mode
   before entering a raid.

The installed files should be:

- `BepInEx\plugins\PmcSpawnToggle\PmcSpawnToggle.Client.dll`
- `SPT_Runtime\user\mods\PmcSpawnToggle\PmcSpawnToggle.dll`

## Requirements

- SPT 4.1.2
- No spawning overhaul is required

Spawn-overhaul mods that replace SPT's location waves may require their own PMC
settings. PMC Spawn Toggle also checks for late-added SPT location waves while
its disabled mode is active. Version 1.0.1 removes both SPT's custom PMC boss
waves and any normal waves explicitly marked `pmcUSEC` or `pmcBEAR`.

## Building from source

The source contains two projects:

- `PmcSpawnToggle` — .NET 10 SPT server module
- `PmcSpawnToggle.Client` — .NET Standard 2.1 BepInEx F12 module

Both projects default to `C:\SPT4.1.2\SPT`.

```powershell
dotnet build .\PmcSpawnToggle\PmcSpawnToggle.csproj -c Release
dotnet build .\PmcSpawnToggle.Client\PmcSpawnToggle.Client.csproj -c Release
```

Created by BensBurnedWaffles.

Licensed under the MIT License.
