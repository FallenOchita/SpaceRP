using SpaceRP.Contracts;

var builder = WebApplication.CreateBuilder(args);
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

app.Run();

public partial class Program
{
}
