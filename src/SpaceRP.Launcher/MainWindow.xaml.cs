using System.Diagnostics;
using System.Security.Cryptography;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using SpaceRP.Launcher.Models;
using SpaceRP.Launcher.Services;

namespace SpaceRP.Launcher;

public partial class MainWindow : Window
{
    private static readonly HttpClient AuthenticationHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly SteamLibraryDiscovery _discovery = new();
    private readonly LauncherSettingsStore _settingsStore = new();
    private readonly DeviceAuthorizationClient _deviceAuthorization =
        new(AuthenticationHttpClient);
    private readonly ProtectedSessionStore _sessionStore = new();
    private CancellationTokenSource? _signInCancellation;
    private IReadOnlyList<GameInstallation> _installations = [];
    private GameInstallation? _selectedGame;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ScanInstallations();
            RefreshAccountStatus();
        };
        Closed += (_, _) => _signInCancellation?.Cancel();
    }

    private void RefreshAccountStatus()
    {
        try
        {
            var savedSession = _sessionStore.Load();
            AccountStatus.Text = string.IsNullOrEmpty(savedSession)
                ? "Not signed in"
                : "A protected session is stored on this Windows account";
            AccountHint.Text = string.IsNullOrEmpty(savedSession)
                ? "Sign in through Steam in your browser to link this launcher."
                : "The session is encrypted with Windows DPAPI. Sign in again if it has expired.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            AccountStatus.Text = "Unable to read the saved session";
            AccountHint.Text = exception.Message;
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (_signInCancellation is not null)
            return;

        if (!Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps &&
             !(serverUri.IsLoopback && serverUri.Scheme == Uri.UriSchemeHttp)) ||
            !string.IsNullOrEmpty(serverUri.UserInfo) ||
            !string.IsNullOrEmpty(serverUri.Query) ||
            !string.IsNullOrEmpty(serverUri.Fragment))
        {
            MessageBox.Show(
                "Enter a valid HTTPS SpaceRP server address. HTTP is allowed only for localhost development.",
                "SpaceRP · Invalid server address",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        serverUri = new Uri(serverUri.GetLeftPart(UriPartial.Authority) + "/");
        _signInCancellation = new CancellationTokenSource();
        SignInButton.IsEnabled = false;
        ServerUrlBox.IsEnabled = false;

        try
        {
            AccountStatus.Text = "Requesting a secure sign-in code...";
            AccountHint.Text = "SpaceRP will open your browser. Your Steam password is entered only on Steam.";

            var cancellationToken = _signInCancellation.Token;
            var device = await _deviceAuthorization.RequestDeviceCodeAsync(
                serverUri, cancellationToken);

            if (string.IsNullOrWhiteSpace(device.DeviceCode) ||
                string.IsNullOrWhiteSpace(device.UserCode) ||
                !Uri.TryCreate(device.VerificationUri, UriKind.RelativeOrAbsolute, out var verificationUri))
            {
                throw new InvalidOperationException("The server returned an invalid device authorization response.");
            }

            verificationUri = verificationUri.IsAbsoluteUri
                ? verificationUri
                : new Uri(serverUri, verificationUri);

            if (verificationUri.Scheme != Uri.UriSchemeHttps &&
                !(verificationUri.IsLoopback && verificationUri.Scheme == Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("The verification page must use HTTPS (or localhost for development).");
            }

            if (!string.Equals(
                    verificationUri.GetLeftPart(UriPartial.Authority),
                    serverUri.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The verification page must belong to the configured SpaceRP server.");
            }

            var separator = string.IsNullOrEmpty(verificationUri.Query) ? "?" : "&";
            var browserUri = new Uri(
                verificationUri + separator + "user_code=" + Uri.EscapeDataString(device.UserCode));

            AccountStatus.Text = $"Enter code: {device.UserCode}";
            AccountHint.Text = "Complete Steam sign-in and authorize this device in the browser. This window will wait for approval.";

            Process.Start(new ProcessStartInfo(browserUri.ToString())
            {
                UseShellExecute = true
            });

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(device.ExpiresIn, 1, 600));
            var session = await _deviceAuthorization.PollForTokenAsync(
                serverUri,
                device.DeviceCode,
                device.Interval,
                expiresAt,
                cancellationToken);

            if (session is null || !string.Equals(
                    session.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The server did not return a supported session token.");
            }

            _sessionStore.Save(session.AccessToken);
            AccountStatus.Text = "Signed in · Session protected";
            AccountHint.Text = "Your SpaceRP session is encrypted for this Windows user.";
        }
        catch (OperationCanceledException)
        {
            AccountStatus.Text = "Sign-in cancelled";
            AccountHint.Text = "You can start Steam sign-in again at any time.";
        }
        catch (Exception exception)
        {
            AccountStatus.Text = "Sign-in could not be completed";
            AccountHint.Text = exception.Message;
        }
        finally
        {
            _signInCancellation.Dispose();
            _signInCancellation = null;
            SignInButton.IsEnabled = true;
            ServerUrlBox.IsEnabled = true;
        }
    }

    private void ScanInstallations()
    {
        try
        {
            var discovered = _discovery.FindInstallations();
            var savedPaths = _settingsStore.LoadManualInstallPaths();

            _installations = discovered
                .Select(installation => ApplySavedPath(installation, savedPaths))
                .ToArray();

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

    private static GameInstallation ApplySavedPath(
        GameInstallation discovered,
        IReadOnlyDictionary<uint, string> savedPaths)
    {
        if (!savedPaths.TryGetValue(discovered.SteamAppId, out var savedPath) ||
            !Directory.Exists(savedPath))
        {
            return discovered;
        }

        var executablePath = GameInstallationValidator.FindGameExecutable(
            discovered.GameName,
            savedPath);

        return discovered with
        {
            InstallPath = Path.GetFullPath(savedPath),
            IsInstalled = true,
            ExecutablePath = executablePath ?? string.Empty,
            HasGameExecutable = executablePath is not null
        };
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

        try
        {
            _settingsStore.SaveManualInstallPath(appId, selectedPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(
                $"The folder is selected for this session, but SpaceRP could not save it for next time.{Environment.NewLine}{exception.Message}",
                "SpaceRP · Settings could not be saved",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

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
