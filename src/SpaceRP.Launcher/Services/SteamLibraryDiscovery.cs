using System.Text.RegularExpressions;
using Microsoft.Win32;
using SpaceRP.Launcher.Models;

namespace SpaceRP.Launcher.Services;

/// <summary>
/// Finds Steam libraries and checks their app manifests. This is discovery,
/// not a full integrity check or proof that a game can launch.
/// </summary>
public sealed class SteamLibraryDiscovery
{
    private static readonly (string Name, uint AppId, string Folder)[] Games =
    [
        ("Space Engineers", 244850, "SpaceEngineers"),
        ("Space Engineers 2", 1133870, "SpaceEngineers2")
    ];

    public IReadOnlyList<GameInstallation> FindInstallations()
    {
        var libraries = FindSteamLibraries();
        var results = new List<GameInstallation>();

        foreach (var game in Games)
        {
            var foundPath = libraries
                .Select(library => FindGamePath(library, game.AppId, game.Folder))
                .FirstOrDefault(path => path is not null);

            results.Add(new GameInstallation(
                game.Name,
                game.AppId,
                foundPath ?? string.Empty,
                foundPath is not null));
        }

        return results;
    }

    private static IEnumerable<string> FindSteamLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRegistryPath(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddRegistryPath(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var defaultSteam = Path.Combine(programFiles, "Steam");
            if (Directory.Exists(defaultSteam))
                roots.Add(defaultSteam);
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            libraries.Add(Path.GetFullPath(root));
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
                continue;

            var contents = File.ReadAllText(libraryFile);
            foreach (Match match in Regex.Matches(contents, "\\"path\\"\\s*\\"([^\\"]+)\\""))
            {
                var path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path))
                    libraries.Add(Path.GetFullPath(path));
            }
        }

        return libraries;
    }

    private static void AddRegistryPath(
        ISet<string> roots,
        RegistryKey hive,
        string subKey,
        string valueName)
    {
        using var key = hive.OpenSubKey(subKey);
        var value = key?.GetValue(valueName) as string;
        if (!string.IsNullOrWhiteSpace(value))
            roots.Add(value.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string? FindGamePath(string library, uint appId, string fallbackFolder)
    {
        var steamApps = Path.Combine(library, "steamapps");
        var manifest = Path.Combine(steamApps, $"appmanifest_{appId}.acf");
        if (!File.Exists(manifest))
            return null;

        var contents = File.ReadAllText(manifest);
        var installDir = Regex.Match(contents, "\\"installdir\\"\\s*\\"([^\\"]+)\\"");
        var folder = installDir.Success ? installDir.Groups[1].Value : fallbackFolder;
        var installPath = Path.Combine(steamApps, "common", folder);

        return Directory.Exists(installPath) ? Path.GetFullPath(installPath) : null;
    }
}
