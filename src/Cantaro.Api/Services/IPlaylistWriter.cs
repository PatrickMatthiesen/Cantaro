namespace Cantaro.Api.Services;

public interface IPlaylistWriter
{
    string PlatformId { get; }

    Task ValidateCreationAsync(
        PlatformAccountContext account,
        CancellationToken cancellationToken);

    Task<string> CreatePlaylistAsync(
        PlatformAccountContext account,
        string name,
        CancellationToken cancellationToken);

    Task ValidateDestinationAsync(
        PlatformAccountContext account,
        string playlistId,
        CancellationToken cancellationToken);

    // Re-read provider state on every attempt and converge on this exact ordered list.
    Task ReconcileAsync(
        PlatformAccountContext account,
        string playlistId,
        IReadOnlyList<string> trackIds,
        CancellationToken cancellationToken);
}
