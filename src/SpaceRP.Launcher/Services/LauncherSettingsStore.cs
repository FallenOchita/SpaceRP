using System.IO;
using System.Text.Json;

namespace SpaceRP.Launcher.Services;

/// <summary>
/// Persists manually selected game folders in the current user's local app-data
/// directory. Settings are local to this Windows user and machine.
/// </summary>
public sealed class LauncherSettingsStore
{
    private readonly string _settingsPath;

    public LauncherSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(appData, "SpaceRP", "launcher-settings.json");
    }

    public IReadOnlyDictionary<uint, string> LoadManualInstallPaths()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new Dictionary<uint, string>();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<SettingsDocument>(json);
            if (settings?.ManualInstallPaths is null)
                return new Dictionary<uint, string>();

            var result = new Dictionary<uint, string>();
            foreach (var entry in settings.ManualInstallPaths)
            {
                if (!uint.TryParse(entry.Key, out var appId) ||
                    (appId != 244850 && appId != 1133870) ||
                    string.IsNullOrWhiteSpace(entry.Value) ||
                    !Path.IsPathFullyQualified(entry.Value))
                {
                    continue;
                }

                result[appId] = entry.Value;
            }

            return result;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // A damaged or inaccessible settings file must not prevent launcher startup.
            return new Dictionary<uint, string>();
        }
    }

    public void SaveManualInstallPath(uint appId, string installPath)
    {
        if (appId != 244850 && appId != 1133870)
            throw new ArgumentOutOfRangeException(nameof(appId), "Unsupported Space Engineers app ID.");

        if (string.IsNullOrWhiteSpace(installPath) || !Path.IsPathFullyQualified(installPath))
            throw new ArgumentException("The installation path must be an absolute path.", nameof(installPath));

        var paths = LoadManualInstallPaths().ToDictionary(
            entry => entry.Key.ToString(),
            entry => entry.Value,
            StringComparer.Ordinal);
        paths[appId.ToString()] = Path.GetFullPath(installPath);

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Unable to resolve the settings directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        var json = JsonSerializer.Serialize(
            new SettingsDocument { ManualInstallPaths = paths },
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }

    private sealed class SettingsDocument
    {
        public Dictionary<string, string> ManualInstallPaths { get; init; } = new();
    }
}
