using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SpaceRP.Launcher.Services;

/// <summary>
/// Stores the SpaceRP bearer session protected for the current Windows user.
/// The token is never written to the settings JSON or a plaintext file.
/// </summary>
public sealed class ProtectedSessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SpaceRP.Launcher.Session.v1");
    private readonly string _sessionPath;

    public ProtectedSessionStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _sessionPath = Path.Combine(appData, "SpaceRP", "session.bin");
    }

    public void Save(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A non-empty session token is required.", nameof(token));

        var directory = Path.GetDirectoryName(_sessionPath)
            ?? throw new InvalidOperationException("Unable to resolve the session directory.");
        Directory.CreateDirectory(directory);

        var plaintext = Encoding.UTF8.GetBytes(token);
        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var temporaryPath = _sessionPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, _sessionPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public string? Load()
    {
        if (!File.Exists(_sessionPath))
            return null;

        var protectedBytes = File.ReadAllBytes(_sessionPath);
        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            Delete();
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Delete()
    {
        if (File.Exists(_sessionPath))
            File.Delete(_sessionPath);
    }
}
