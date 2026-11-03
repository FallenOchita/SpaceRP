namespace SpaceRP.Launcher.Models;

public sealed record GameInstallation(
    string GameName,
    uint SteamAppId,
    string InstallPath,
    bool IsInstalled);
