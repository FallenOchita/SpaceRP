using System.Text.Json.Serialization;
using SpaceRP.Contracts;
using SpaceRP.Server.Accounts;
using SpaceRP.Server.Authentication;

const string SessionCookieName = "__Host-SpaceRPSession";
var sessionLifetime = TimeSpan.FromHours(12);

var builder = WebApplication.CreateBuilder(args);
var steamOptions = new SteamOpenIdOptions
{
    Realm = builder.Configuration["Steam:Realm"] ?? string.Empty,
    CallbackUrl = builder.Configuration["Steam:CallbackUrl"] ?? string.Empty
};

builder.Services.AddSingleton(steamOptions);
builder.Services.AddHttpClient(nameof(SteamOpenIdService), client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceRP/0.1 SteamOpenID");
});
builder.Services.AddSingleton<SteamOpenIdService>();
builder.Services.AddSingleton<SpaceRpAccountStore>();
builder.Services.AddSingleton<DeviceAuthorizationStore>();

var app = builder.Build();
var accountStore = app.Services.GetRequiredService<SpaceRpAccountStore>();
var deviceStore = app.Services.GetRequiredService<DeviceAuthorizationStore>();
await accountStore.InitializeAsync();
await deviceStore.InitializeAsync();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "SpaceRP.Server"
}));

app.MapGet("/api/v1/platform", () => Results.Ok(
    new PlatformInfo(
        "SpaceRP",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.1.0",
        [GameEdition.SpaceEngineers1, GameEdition.SpaceEngineers2])));

app.MapGet("/auth/steam/login", (SteamOpenIdService steam) =>
{
    try
    {
        return Results.Redirect(steam.CreateLoginUri().ToString());
    }
    catch (InvalidOperationException)
    {
        return Results.Problem(
            "Steam sign-in is not configured. Set Steam:Realm and Steam:CallbackUrl to HTTPS URLs.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/auth/steam/callback", async (
    HttpRequest request,
    HttpResponse response,
    SteamOpenIdService steam,
    SpaceRpAccountStore accounts,
    CancellationToken cancellationToken) =>
{
    try
    {
        var steamId = await steam.ValidateCallbackAsync(request.Query, cancellationToken);
        if (steamId is null)
            return Results.Problem("Steam sign-in could not be verified.", statusCode: StatusCodes.Status401Unauthorized);

        var session = await accounts.CreateSessionAsync(steamId, sessionLifetime, cancellationToken);
        response.Cookies.Append(SessionCookieName, session.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = session.ExpiresAt,
            MaxAge = session.ExpiresAt - DateTimeOffset.UtcNow
        });

        return Results.Ok(new
        {
            authenticated = true,
            account = session.Account,
            expiresAt = session.ExpiresAt
        });
    }
    catch (InvalidOperationException)
    {
        return Results.Problem("Steam sign-in is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/v1/account/me", async (
    HttpRequest request,
    SpaceRpAccountStore accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetSessionToken(request);
    if (token is null)
        return Results.Unauthorized();

    var account = await accounts.FindAccountBySessionAsync(token, cancellationToken);
    return account is null
        ? Results.Unauthorized()
        : Results.Ok(new { authenticated = true, account });
});

app.MapPost("/auth/steam/logout", async (
    HttpRequest request,
    HttpResponse response,
    SpaceRpAccountStore accounts,
    CancellationToken cancellationToken) =>
{
    var token = GetSessionToken(request);
    if (token is not null)
        await accounts.RevokeSessionAsync(token, cancellationToken);

    response.Cookies.Delete(SessionCookieName, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    });

    return Results.Ok(new { authenticated = false });
});

app.MapPost("/auth/device/code", async (
    DeviceAuthorizationStore devices,
    CancellationToken cancellationToken) =>
{
    var authorization = await devices.CreateAsync(cancellationToken);
    var verificationUri = app.Configuration["SpaceRP:DeviceVerificationUri"] ?? "/auth/device/verify";
    return Results.Ok(new
    {
        device_code = authorization.DeviceCode,
        user_code = authorization.UserCode,
        verification_uri = verificationUri,
        expires_in = Math.Max(0, (int)(authorization.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds),
        interval = 5
    });
});

app.MapGet("/auth/device/verify", (string? user_code) =>
{
    var code = System.Net.WebUtility.HtmlEncode(user_code ?? string.Empty);
    var html = $$"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>SpaceRP device sign-in</title>
        <style>
        body{font-family:Segoe UI,Arial,sans-serif;background:#101820;color:#eef4f7;display:grid;place-items:center;min-height:100vh;margin:0}
        main{background:#1c2b36;padding:32px;border:1px solid #38515f;border-radius:14px;width:min(420px,calc(100% - 48px))}
        h1{color:#56e0c0}input,button{box-sizing:border-box;width:100%;padding:13px;margin-top:12px;border-radius:7px;font-size:16px}
        input{background:#101820;color:white;border:1px solid #58707c}button{background:#56e0c0;color:#101820;border:0;font-weight:700;cursor:pointer}
        a{color:#56e0c0}
        </style></head>
        <body><main><h1>SPACERP</h1><h2>Authorize your launcher</h2>
        <p>Sign in with Steam in this browser first, then enter the code shown by your SpaceRP launcher.</p>
        <p><a href="/auth/steam/login">Sign in with Steam</a></p>
        <form method="post" action="/auth/device/verify">
        <label for="user_code">Device code</label>
        <input id="user_code" name="user_code" value="{{code}}" maxlength="9" autocomplete="one-time-code" required>
        <button type="submit">AUTHORIZE DEVICE</button></form></main></body></html>
        """;
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/auth/device/verify", async (
    HttpRequest request,
    SpaceRpAccountStore accounts,
    DeviceAuthorizationStore devices,
    CancellationToken cancellationToken) =>
{
    var token = request.Cookies.TryGetValue(SessionCookieName, out var cookieToken)
        ? cookieToken
        : null;
    if (token is null)
        return Results.Content(
            "<!doctype html><html><body><p>Sign in with Steam first, then return to this page.</p><a href='/auth/steam/login'>Sign in with Steam</a></body></html>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status401Unauthorized);

    var account = await accounts.FindAccountBySessionAsync(token, cancellationToken);
    if (account is null)
        return Results.Unauthorized();

    var form = await request.ReadFormAsync(cancellationToken);
    var userCode = form["user_code"].ToString();
    var approved = await devices.ApproveAsync(userCode, account.AccountId, cancellationToken);
    var message = approved
        ? "This device is authorized. You can return to the SpaceRP launcher."
        : "That code is invalid, expired, or has already been used.";
    var html = $"<!doctype html><html lang='en'><meta charset='utf-8'><title>SpaceRP device authorization</title><body style='font-family:Segoe UI,Arial;background:#101820;color:#eef4f7;padding:48px'><h1>SPACERP</h1><p>{message}</p></body></html>";
    return Results.Content(html, "text/html; charset=utf-8",
        statusCode: approved ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

app.MapPost("/auth/device/token", async (
    DeviceTokenRequest body,
    DeviceAuthorizationStore devices,
    SpaceRpAccountStore accounts,
    CancellationToken cancellationToken) =>
{
    var result = await devices.PollAsync(body.DeviceCode, cancellationToken);
    if (result.Status != DeviceAuthorizationStatus.Approved || result.AccountId is null)
    {
        var error = result.Status switch
        {
            DeviceAuthorizationStatus.Pending => "authorization_pending",
            DeviceAuthorizationStatus.Expired => "expired_token",
            DeviceAuthorizationStatus.AlreadyConsumed => "invalid_grant",
            _ => "invalid_grant"
        };
        return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
    }

    var account = await accounts.FindAccountByIdAsync(result.AccountId.Value, cancellationToken);
    if (account is null)
        return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);

    var session = await accounts.CreateSessionAsync(account.SteamId64, sessionLifetime, cancellationToken);
    return Results.Ok(new
    {
        access_token = session.Token,
        token_type = "Bearer",
        expires_in = Math.Max(0, (int)(session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds),
        account = session.Account
    });
});

app.Run();

static string? GetSessionToken(HttpRequest request)
{
    if (request.Cookies.TryGetValue("__Host-SpaceRPSession", out var cookieToken) &&
        !string.IsNullOrWhiteSpace(cookieToken))
    {
        return cookieToken;
    }

    var authorization = request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? authorization[prefix.Length..].Trim()
        : null;
}

public sealed record DeviceTokenRequest([property: JsonPropertyName("device_code")] string DeviceCode);

public partial class Program
{
}
