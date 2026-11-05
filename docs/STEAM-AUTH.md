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

## Desktop launcher device authorization

The server now exposes a device-authorization flow so a desktop launcher can sign in without reading browser cookies or asking the player for a Steam password.

1. The launcher calls `POST /auth/device/code`. The response contains a one-time `device_code`, a short `user_code`, a verification URI, a 10-minute expiry, and a suggested 5-second polling interval.
2. The player opens the verification URI in a browser. If needed, they sign in with Steam there, then submit the user code. The verification form requires the existing SpaceRP session cookie.
3. The browser approval binds the pending device request to the authenticated account. User codes are normalized, expire, and can be approved only once.
4. The launcher polls `POST /auth/device/token` with JSON `{ "device_code": "..." }`. A pending request returns `authorization_pending`; an approved request returns one bearer session token; expired, invalid, or consumed requests return an error.
5. The launcher uses that opaque bearer token in the `Authorization: Bearer` header for authenticated API calls. The existing account endpoint accepts either the secure browser cookie or the bearer session.

The database stores only a SHA-256 hash of the device code. The raw device code is returned only when it is created. User codes are not credentials: approval still requires a valid, authenticated browser session. Device authorization records expire after ten minutes and a successful poll consumes the authorization so it cannot issue a second token.

The desktop client integration, secure Windows token storage (DPAPI), polling backoff, and sign-in UX are the next launcher milestone. Do not store bearer tokens in plain-text launcher settings or logs.
