# SpaceRP architecture

## High-level boundaries

- **Launcher:** desktop client, game selection, local installation detection, account linking, update verification, and connection handoff.
- **Platform API:** account and session services, server registration, permissions, and control-plane APIs. Hosted separately from player machines.
- **Game server components:** game-specific integration and runtime components for SE1 and SE2. They remain separate because their APIs and lifecycle behavior may differ.
- **Plugin runtime:** language-specific execution hosts behind a shared permission and capability model.

## Trust boundaries

The launcher is an untrusted client. The server must validate authentication tokens, permissions, and all client-provided data. Steam identity must be established through a verifiable Steam authentication flow; a SteamID supplied directly by a client is not proof of ownership.

Plugins are untrusted workloads. A language runtime alone is not a security boundary. Before third-party plugins are enabled, SpaceRP needs isolation, explicit capabilities, resource limits, lifecycle controls, logging, and a threat model.

## Initial implementation

The first scaffold contains shared contracts, separate adapter projects, and a minimal ASP.NET Core host. The adapters are placeholders and do not yet load or control either game.
