using SpaceRP.Contracts;
using SpaceRP.GameAdapters.Abstractions;

namespace SpaceRP.GameAdapters.SE2;

/// <summary>
/// SE2 adapter boundary. Real validation and game integration will be added
/// only after the supported SE2 installation and API requirements are verified.
/// </summary>
public sealed class Se2GameAdapter : IGameAdapter
{
    public GameEdition Game => GameEdition.SpaceEngineers2;

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
