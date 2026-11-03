# SpaceRP Windows launcher

The first launcher milestone adds a Windows WPF shell with SE1 / SE2 selection and Steam library discovery.

## Detection behavior

- Reads common Steam install locations from the current-user and local-machine registry.
- Checks the default Steam library and additional libraries listed in `steamapps/libraryfolders.vdf`.
- Uses the Steam app manifests to locate each game independently.
- Does not treat a directory alone as proof of a valid or launchable installation.
- Does not sign in to Steam, verify ownership, launch a game, or connect to a SpaceRP server yet.

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
