using System.Windows;
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

        target.Text = installation?.IsInstalled == true
            ? "Installed · Ready to select"
            : "Not detected · Check Steam installation";
    }

    private void SelectSe1_Click(object sender, RoutedEventArgs e) => SelectGame("Space Engineers");

    private void SelectSe2_Click(object sender, RoutedEventArgs e) => SelectGame("Space Engineers 2");

    private void SelectGame(string gameName)
    {
        _selectedGame = _installations.FirstOrDefault(
            item => string.Equals(item.GameName, gameName, StringComparison.Ordinal));
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
        InstallPathStatus.Text = _selectedGame.IsInstalled
            ? _selectedGame.InstallPath
            : "This game was not found in the detected Steam libraries.";
        ContinueButton.IsEnabled = _selectedGame.IsInstalled;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => ScanInstallations();

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null || !_selectedGame.IsInstalled)
            return;

        MessageBox.Show(
            $"{_selectedGame.GameName} was detected at:{Environment.NewLine}{_selectedGame.InstallPath}{Environment.NewLine}{Environment.NewLine}The SpaceRP connection flow is the next launcher milestone.",
            "SpaceRP · Installation detected",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
