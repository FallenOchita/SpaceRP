using SpaceRP.Contracts;

namespace SpaceRP.GameAdapters.Abstractions;

public interface IGameAdapter
{
    GameEdition Game { get; }

    ValueTask<bool> ValidateInstallationAsync(
        string installationPath,
        CancellationToken cancellationToken = default);
}
