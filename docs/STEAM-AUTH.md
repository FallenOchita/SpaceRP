# Steam account linking and SpaceRP sessions

SpaceRP uses Steam OpenID to verify a player's Steam identity. Players authenticate on Steam; SpaceRP never requests or stores Steam passwords.

## Server configuration

Configure these values through environment variables or the deployment's secret/configuration provider:

- `Steam__Realm`: the public HTTPS origin hosting the SpaceRP API, for example `https://rp.example.com/`.
- `Steam__CallbackUrl`: the exact public HTTPS callback URL, for example `https://rp.example.com/auth/steam/callback`.
- `SpaceRP__DatabasePath`: optional path to the SQLite database. The default is `data/spacerp.db` beneath the server application directory. The server creates the parent directory when needed.

The callback URL must be registered with the Steam OpenID relying-party configuration used by the deployment. Serve authentication over HTTPS in production.

## Sign-in and session flow

1. `GET /auth/steam/login` creates a one-time random state and redirects the browser to Steam.
2. `GET /auth/steam/callback` validates the returned state, callback URL, OpenID response, SteamID64 format, nonce freshness/replay, and Steam's server-to-server `check_authentication` response.
3. After successful verification, SpaceRP creates or retrieves the account associated with that SteamID64 and creates a 12-hour session.
4. The server sends the opaque session credential in the `__Host-SpaceRPSession` cookie. The cookie is Secure, HttpOnly, SameSite=Lax, and scoped to `/`. The callback response contains the account profile and expiry, never the raw session credential.

The database stores only a SHA-256 hash of each random session credential. It does not store the credential itself. A SteamID64 is an account identifier, not a bearer credential.

## Account and session endpoints

- `GET /api/v1/account/me`: returns the authenticated account profile when a valid session cookie is present; otherwise returns 401.
- `POST /auth/steam/logout`: revokes the current session and clears the cookie.
- `GET /auth/steam/login`: begins Steam sign-in.
- `GET /auth/steam/callback`: verifies Steam and establishes the SpaceRP session.

Accounts are created automatically on a player's first verified Steam sign-in. Repeated sign-ins resolve to the same account because SteamID64 has a unique database constraint. Multiple sessions may exist for one account; logging out revokes only the current session.

## Storage and deployment boundary

SQLite provides durable account and session storage for a single server host. Back up the database and place it on persistent storage; do not put it in a temporary deployment directory. SQLite is not a shared multi-host database.

Steam OpenID pending-state and nonce replay tracking are still process-local. Keep the authentication service single-instance until those short-lived values move to shared expiring storage. Multi-instance deployment also requires a shared database service and coordinated state storage.

The launcher does not yet perform the desktop sign-in handoff or consume this cookie-based session. Game-server authorization, session refresh, account recovery, and administrative account management are separate future milestones.
