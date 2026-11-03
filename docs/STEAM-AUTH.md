# Steam account linking

SpaceRP uses Steam OpenID for identity verification. Players are redirected to Steam to authenticate there; SpaceRP never requests or stores a Steam password.

## Server configuration

Configure these values through environment variables or the deployment's secret/configuration provider:

- `Steam__Realm`: the public HTTPS origin hosting the SpaceRP API, for example `https://rp.example.com/`.
- `Steam__CallbackUrl`: the exact public HTTPS callback URL, for example `https://rp.example.com/auth/steam/callback`.

The callback URL must be registered with the Steam OpenID relying-party configuration used by the deployment. Do not expose the callback over plain HTTP in production.

## Endpoints

- `GET /auth/steam/login`: creates a one-time random state and redirects the browser to Steam.
- `GET /auth/steam/callback`: validates the returned state, exact callback URL, OpenID response, SteamID64 format, response nonce freshness/replay, and Steam's server-to-server `check_authentication` response.

A successful callback currently returns the verified SteamID64 as JSON. This is an identity-verification milestone, **not yet a SpaceRP login session**. Do not treat the returned SteamID as a bearer credential. The next authentication milestone must exchange the verified identity for a short-lived, server-issued session and securely hand that session to the launcher.

Pending states and replay tracking are held in process memory. Run a single API instance until shared, expiring state storage is implemented; multi-instance deployments need a shared store. Configure HTTPS termination and trusted proxy handling at the deployment edge.
