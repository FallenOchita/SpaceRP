using SpaceRP.Contracts;
using SpaceRP.Server.Authentication;

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

var app = builder.Build();

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
    SteamOpenIdService steam,
    CancellationToken cancellationToken) =>
{
    try
    {
        var steamId = await steam.ValidateCallbackAsync(request.Query, cancellationToken);
        return steamId is null
            ? Results.Problem("Steam sign-in could not be verified.", statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(new { provider = "steam", steamId, verified = true });
    }
    catch (InvalidOperationException)
    {
        return Results.Problem("Steam sign-in is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public partial class Program
{
}
