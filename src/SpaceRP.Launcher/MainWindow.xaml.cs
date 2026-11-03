using System.IO;
using System.Windows;
using Microsoft.Win32;
using SpaceRP.Launcher.Models;
using SpaceRP.Launcher.Services;

namespace SpaceRP.Launcher;

public partial class MainWindow : Window
{
    private readonly SteamLibraryDiscovery _discovery = new();
    private IReadOnlyList<GameInstallation> _installations = [];
    private GameInstallation? _selectedGame;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ScanInstallations();
    }

    private void ScanInstallations()
    {
        try
        {
            _installations = _discovery.FindInstallations();
            UpdateGameStatus("Space Engineers", Se1Status);
            UpdateGameStatus("Space Engineers 2", Se2Status);
        }
        catch (Exception exception)
        {
            Se1Status.Text = "Unable to scan Steam libraries.";
            Se2Status.Text = "Unable to scan Steam libraries.";
            InstallPathStatus.Text = exception.Message;
        }

        UpdateSelection();
    }

    private void UpdateGameStatus(string gameName, System.Windows.Controls.TextBlock target)
    {
        var installation = _installations.FirstOrDefault(
            item => string.Equals(item.GameName, gameName, StringComparison.Ordinal));

        target.Text = installation?.HasGameExecutable == true
            ? "Installed · Executable verified"
            : installation?.IsInstalled == true
                ? "Folder found · Game executable missing"
                : "Not detected · Browse for the game folder";
    }

    private void SelectSe1_Click(object sender, RoutedEventArgs e) => SelectGame("Space Engineers");

    private void SelectSe2_Click(object sender, RoutedEventArgs e) => SelectGame("Space Engineers 2");

    private void BrowseSe1_Click(object sender, RoutedEventArgs e) => BrowseForGameFolder("Space Engineers");

    private void BrowseSe2_Click(object sender, RoutedEventArgs e) => BrowseForGameFolder("Space Engineers 2");

    private void SelectGame(string gameName)
    {
        _selectedGame = _installations.FirstOrDefault(
            item => string.Equals(item.GameName, gameName, StringComparison.Ordinal));
        UpdateSelection();
    }

    private void BrowseForGameFolder(string gameName)
    {
        var existing = _installations.FirstOrDefault(
            item => string.Equals(item.GameName, gameName, StringComparison.Ordinal));

        var dialog = new OpenFolderDialog
        {
            Title = $"Select the {gameName} installation folder",
            Multiselect = false,
            InitialDirectory = existing?.IsInstalled == true && Directory.Exists(existing.InstallPath)
                ? existing.InstallPath
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var selectedPath = Path.GetFullPath(dialog.FolderName);
        if (!Directory.Exists(selectedPath))
        {
            MessageBox.Show(
                "The selected folder does not exist. Choose the game's installation folder.",
                "SpaceRP · Invalid folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var appId = gameName == "Space Engineers" ? 244850u : 1133870u;
        var executablePath = GameInstallationValidator.FindGameExecutable(gameName, selectedPath);
        var manualInstallation = new GameInstallation(
            gameName,
            appId,
            selectedPath,
            true,
            executablePath ?? string.Empty,
            executablePath is not null);
        _installations = _installations
            .Where(item => !string.Equals(item.GameName, gameName, StringComparison.Ordinal))
            .Append(manualInstallation)
            .ToArray();

        UpdateGameStatus("Space Engineers", Se1Status);
        UpdateGameStatus("Space Engineers 2", Se2Status);
        _selectedGame = manualInstallation;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (_selectedGame is null)
        {
            SelectionStatus.Text = "No game selected";
            InstallPathStatus.Text = "Select a game above to continue.";
            ContinueButton.IsEnabled = false;
            return;
        }

        SelectionStatus.Text = _selectedGame.GameName;
        InstallPathStatus.Text = !_selectedGame.IsInstalled
            ? "This game was not found in the detected Steam libraries."
            : _selectedGame.HasGameExecutable
                ? $"{_selectedGame.InstallPath}{Environment.NewLine}Executable: {_selectedGame.ExecutablePath}"
                : $"{_selectedGame.InstallPath}{Environment.NewLine}Game executable not found in the expected location.";
        ContinueButton.IsEnabled = _selectedGame.HasGameExecutable;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => ScanInstallations();

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null || !_selectedGame.HasGameExecutable)
            return;

        MessageBox.Show(
            $"{_selectedGame.GameName} executable was found at:{Environment.NewLine}{_selectedGame.ExecutablePath}{Environment.NewLine}{Environment.NewLine}This check confirms that the expected executable file exists. SpaceRP has not verified file integrity or launched the game yet.",
            "SpaceRP · Installation validated",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
