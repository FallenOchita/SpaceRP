using System.IO;

namespace SpaceRP.Launcher.Services;

/// <summary>
/// Checks known Windows game executable locations. This confirms file presence only;
/// it does not verify file integrity, signatures, dependencies, or successful startup.
/// </summary>
public static class GameInstallationValidator
{
    public static string? FindGameExecutable(string gameName, string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return null;

        IEnumerable<string> candidates = gameName switch
        {
            "Space Engineers" =>
            [
                Path.Combine(installPath, "Bin64", "SpaceEngineers.exe"),
                Path.Combine(installPath, "Bin", "SpaceEngineers.exe")
            ],
            "Space Engineers 2" =>
            [
                Path.Combine(installPath, "Game2", "SpaceEngineers2.exe")
            ],
            _ => []
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
