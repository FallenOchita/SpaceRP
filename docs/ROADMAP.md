# SpaceRP delivery roadmap

## Milestone 0 — Repository foundation
- [x] Establish a clean repository and MIT license.
- [x] Separate shared contracts, server host, and SE1/SE2 adapter projects.
- [ ] Verify CI build on GitHub Actions.

## Milestone 1 — Platform service
- [ ] Configuration and structured logging.
- [ ] Persistent account and server registry.
- [ ] Authentication/session model and API versioning.
- [ ] Service deployment and health monitoring for an external host.

## Milestone 2 — Launcher
- [ ] Original SpaceRP branding, icon, and loading/error screens.
- [ ] SE1 / SE2 selection as the first player-facing screen.
- [ ] Steam library discovery and installation validation.
- [ ] Secure Steam account linking.
- [ ] Connection handoff to the selected game's SpaceRP service.

## Milestone 3 — Game integrations
- [ ] Verify supported SE1 API and dedicated-server integration.
- [ ] Implement and test the SE1 adapter.
- [ ] Verify supported SE2 API and dedicated-server integration.
- [ ] Implement and test the SE2 adapter.

## Milestone 4 — Extensibility
- [ ] C# plugin contract and lifecycle.
- [ ] JavaScript runtime integration.
- [ ] Lua runtime integration.
- [ ] Permission model, isolation, quotas, and audit logging.

## Milestone 5 — Updates and release
- [ ] Signed update manifest and package verification.
- [ ] Atomic update installation and rollback.
- [ ] Release pipeline, versioning, and deployment documentation.
