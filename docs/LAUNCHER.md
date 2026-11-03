# SpaceRP Windows launcher

The launcher milestone provides a Windows WPF shell with SE1 / SE2 selection, Steam library discovery, and manual game-folder selection.

## Detection and selection behavior

- Reads common Steam install locations from the current-user and local-machine registry.
- Checks the default Steam library and additional libraries listed in `steamapps/libraryfolders.vdf`.
- Uses the Steam app manifests to locate each game independently.
- Provides a folder picker for installations that Steam discovery did not find.
- Manual selection verifies that the chosen directory exists and records it for the current launcher session.
- A selected directory is not proof that the game files are complete or that the game can launch.
- Manual selections are session-only; pressing **Rescan** replaces them with fresh Steam discovery results.
- Does not sign in to Steam, verify ownership, validate game executables, launch a game, or connect to a SpaceRP server yet.

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

The launcher currently targets Windows. CI can compile the Windows target from a non-Windows runner with Windows targeting enabled, but the UI itself must be exercised on Windows.
