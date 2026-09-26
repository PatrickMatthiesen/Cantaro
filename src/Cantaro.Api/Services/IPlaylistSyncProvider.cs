using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed record PlaylistRemoteTrack(
    string ExternalId,
    Guid? TrackId,
    Guid? ObservationId,
    string Title,
    int Position,
    bool IsAvailable = true);

public sealed record PlaylistRemoteSnapshot(
    string Id,
    string Name,
    IReadOnlyList<PlaylistRemoteTrack> Tracks,
    string? Revision = null,
    bool IsComplete = true,
    int UnavailableItemCount = 0);

public sealed record PlaylistRemoteCatalogItem(string Id, string Name);

public interface IPlaylistSyncProvider
{
    string PlatformId { get; }
    Task<IReadOnlyList<PlaylistRemoteCatalogItem>> ListPlaylistsAsync(
        PlatformAccountContext account, CancellationToken cancellationToken);
    Task<PlaylistRemoteSnapshot> ReadAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken);
    Task<string?> ResolveAsync(
        PlatformAccountContext account, PlaylistEntry entry, CancellationToken cancellationToken);
    Task RenameAsync(
        PlatformAccountContext account, string playlistId, string name, CancellationToken cancellationToken);
    Task DeleteAsync(
        PlatformAccountContext account, string playlistId, CancellationToken cancellationToken);
}
