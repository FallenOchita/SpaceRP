namespace SpaceRP.Contracts;

public enum GameEdition
{
    SpaceEngineers1 = 1,
    SpaceEngineers2 = 2
}

public sealed record PlatformInfo(
    string Name,
    string Version,
    IReadOnlyList<GameEdition> SupportedGames);

public sealed record PlayerIdentity(
    string SpaceRpUserId,
    string? SteamId);

public sealed record ServerDescriptor(
    string ServerId,
    string DisplayName,
    GameEdition Game,
    Uri Endpoint);
