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

var app = builder.Build();
var accountStore = app.Services.GetRequiredService<SpaceRpAccountStore>();
await accountStore.InitializeAsync();

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
    if (!request.Cookies.TryGetValue(SessionCookieName, out var token))
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
    if (request.Cookies.TryGetValue(SessionCookieName, out var token))
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

app.Run();

public partial class Program
{
}
