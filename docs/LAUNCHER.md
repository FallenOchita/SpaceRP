# SpaceRP Windows launcher

The launcher provides a Windows WPF shell with SE1 / SE2 selection, Steam library discovery, manual game-folder selection, executable-presence validation, and persistent manual installation paths.

## Detection and validation behavior

- Reads common Steam install locations from the current-user and local-machine registry.
- Checks the default Steam library and additional libraries listed in `steamapps/libraryfolders.vdf`.
- Uses the Steam app manifests to locate each game independently.
- Provides a folder picker for installations that Steam discovery did not find.
- Manual selection verifies that the chosen directory exists.
- Checks for the expected Windows game executable:
  - Space Engineers: `Bin64/SpaceEngineers.exe`, with `Bin/SpaceEngineers.exe` as a fallback.
  - Space Engineers 2: `Game2/SpaceEngineers2.exe`.
- Enables **Continue** only when the expected executable file is present.
- Saves manually selected folders in `%LOCALAPPDATA%/SpaceRP/launcher-settings.json`.
- Restores saved manual folders on startup and rechecks the expected executable before marking them ready.
- If a saved folder no longer exists, the launcher falls back to Steam discovery.
- Pressing **Rescan** refreshes Steam discovery while retaining saved manual folder preferences.
- If settings are unreadable or invalid, launcher startup continues with Steam discovery instead.
- Executable presence is not a file-integrity check and does not prove that the game can start.
- Does not sign in to Steam, verify ownership, launch a game, or connect to a SpaceRP server yet.

The settings file contains local installation paths only; it does not contain Steam credentials or authentication tokens.

The executable locations are based on the published Space Engineers troubleshooting guidance and Steam's launch configuration for Space Engineers 2.

Steam application IDs used by the detector:
- Space Engineers: `244850`
- Space Engineers 2: `1133870`

## Build

This is a Windows desktop project targeting .NET 8 WPF:

```powershell
dotnet restore SpaceRP.sln
dotnet build SpaceRP.sln --configuration Release
dotnet run --project src/SpaceRP.Launcher
```

The launcher currently targets Windows. CI can compile the Windows target from a non-Windows runner with Windows targeting enabled, but the UI and installed-game paths must be exercised on Windows.
