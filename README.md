# SpaceRP

**SpaceRP** is an independent roleplay platform for Space Engineers 1 (SE1) and Space Engineers 2 (SE2).

The project is being developed from the ground up. It does not depend on Nexus or Torch and will not copy their source code or implementation.

## Project goals

- A player-facing launcher that presents SE1 and SE2 selection before connecting to the corresponding SpaceRP service.
- Steam account linking and independent detection of installed SE1 and SE2 installations, including Steam libraries.
- Separate server services that can run on a dedicated external PC.
- Game-specific adapters so SE1 and SE2 integrations can evolve independently.
- A safe plugin runtime supporting C#, JavaScript, and Lua.
- A signed, validated launcher updater with rollback-safe installation.
- Original SpaceRP branding, application icon, and loading/error experiences.

## Repository layout

| Path | Responsibility |
| --- | --- |
| `src/SpaceRP.Contracts` | Shared, game-agnostic contracts and data types |
| `src/SpaceRP.Server` | Initial ASP.NET Core service host |
| `src/SpaceRP.GameAdapters.Abstractions` | Contracts implemented by game-specific adapters |
| `src/SpaceRP.GameAdapters.SE1` | SE1 adapter boundary |
| `src/SpaceRP.GameAdapters.SE2` | SE2 adapter boundary |
| `docs/` | Architecture and delivery roadmap |

## Build

Requirements: .NET 8 SDK.

```bash
dotnet restore SpaceRP.sln
dotnet build SpaceRP.sln --configuration Release --no-restore
dotnet run --project src/SpaceRP.Server
```

The initial service exposes `GET /health` and `GET /api/v1/platform`. These are foundation endpoints, not a complete multiplayer backend.

## Development principles

1. Keep game-independent platform logic separate from SE1 and SE2 code.
2. Verify each game integration against the corresponding supported game version and official/publicly available API surface.
3. Do not assume SE1 and SE2 APIs or lifecycle models are interchangeable.
4. Never claim Steam ownership or identity based only on a client-supplied display name; implement a verifiable account-linking flow before enabling trusted identity.
5. Treat scripts and plugins as untrusted code. Runtime isolation, permissions, resource limits, and auditability are release requirements.
6. Keep secrets, signing keys, and production credentials out of source control.

## Status

This repository currently contains the first compilable architecture scaffold. Steam linking, full game detection, launcher UI, updater, persistent accounts, server discovery, game adapters, and scripting engines are planned work and are not implemented yet.
