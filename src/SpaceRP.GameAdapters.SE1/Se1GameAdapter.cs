using SpaceRP.Contracts;
using SpaceRP.GameAdapters.Abstractions;

namespace SpaceRP.GameAdapters.SE1;

/// <summary>
/// SE1 adapter boundary. Real validation and game integration will be added
/// only after the supported SE1 installation and API requirements are verified.
/// </summary>
public sealed class Se1GameAdapter : IGameAdapter
{
    public GameEdition Game => GameEdition.SpaceEngineers1;

    public ValueTask<bool> ValidateInstallationAsync(
        string installationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        cancellationToken.ThrowIfCancellationRequested();

        // A directory check is deliberately not treated as full game validation.
        return ValueTask.FromResult(Directory.Exists(installationPath));
    }
}
