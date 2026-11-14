using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>
/// Enforces canonical playlist membership while preserving every source observation.
/// A playlist contains a canonical Track at most once, even when several provider
/// presentations resolve to that Track.
/// </summary>
public sealed class PlaylistCanonicalReconciliationService(ApplicationDbContext dbContext)
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public IReadOnlyList<PlaylistEntry> ReconcileImportedEntries(IEnumerable<PlaylistEntry> entries, bool allowDuplicates = false)
    {
        var retainedEntries = new List<PlaylistEntry>();
        var retainedCountByPlaylist = new Dictionary<Guid, int>();
        var seenObservationsByPlaylist = new HashSet<(Guid PlaylistId, Guid ObservationId)>();
        var seenTracksByPlaylist = new HashSet<(Guid PlaylistId, Guid TrackId)>();

        foreach (var entry in entries.OrderBy(entry => entry.Position).ThenBy(entry => entry.Id))
        {
            if (!allowDuplicates && entry.TrackObservationId is { } observationId
                && !seenObservationsByPlaylist.Add((entry.PlaylistId, observationId)))
            {
                continue;
            }

            if (!allowDuplicates && entry.TrackId is { } trackId
                && !seenTracksByPlaylist.Add((entry.PlaylistId, trackId)))
            {
                continue;
            }

            entry.Position = retainedCountByPlaylist.GetValueOrDefault(entry.PlaylistId);
            retainedCountByPlaylist[entry.PlaylistId] = entry.Position + 1;
            retainedEntries.Add(entry);
        }

        return retainedEntries;
    }

    public async Task ReconcileObservationAsync(
        Guid observationId,
        Guid? trackId,
        CancellationToken cancellationToken)
    {
        var observation = await _dbContext.TrackObservations.AsNoTracking().Include(o => o.ProviderCatalogItem)
            .SingleOrDefaultAsync(o => o.Id == observationId, cancellationToken);
        if (ObservationInput.Read(observation, observation?.OwnerUserId, DateTimeOffset.UtcNow) is null) return;
        var ownerId = observation!.OwnerUserId;
        var entries = await _dbContext.PlaylistEntries.Include(entry => entry.TrackObservation)
            .ThenInclude(o => o!.ProviderCatalogItem)
            .Include(entry => entry.Track).ThenInclude(track => track!.ProviderItems)
            .Where(entry => entry.TrackObservationId == observationId && entry.Playlist!.UserId == ownerId)
            .ToListAsync(cancellationToken);
        var playlistIdsWithEntries = entries.Select(entry => entry.PlaylistId).Distinct().ToArray();
        var links = await _dbContext.ServicePlaylistMappings.AsNoTracking()
            .Where(link => playlistIdsWithEntries.Contains(link.PlaylistId) && link.UserId == ownerId)
            .ToListAsync(cancellationToken);
        var allPlaylistEntries = await _dbContext.PlaylistEntries
            .Where(entry => playlistIdsWithEntries.Contains(entry.PlaylistId) && entry.OwnerUserId == ownerId)
            .ToListAsync(cancellationToken);
        // A scoped manual choice protects membership even when later input changes
        // make its destination proof ineligible for reuse. This does not grant export permission.
        var protectedEntryIds = allPlaylistEntries.Where(entry => links.Any(link => link.PlaylistId == entry.PlaylistId
            && PlaylistMatchingProgress.Read(link.MatchingProgressJson).TryGetValue(entry.Id, out var saved)
            && saved.ManualConfirmation && saved.ProofVersion == PlaylistMatchingProgress.CurrentProofVersion
            && saved.Scope == PlaylistMatchingProgress.ScopeFor(link, entry)))
            .Select(entry => entry.Id).ToHashSet();
        entries = entries.Where(entry => !protectedEntryIds.Contains(entry.Id)).ToList();

        if (trackId == null)
        {
            foreach (var entry in entries)
            {
                entry.TrackId = null;
            }

            return;
        }

        var entryIds = entries.Select(entry => entry.Id).ToArray();
        var playlistIds = entries.Select(entry => entry.PlaylistId).Distinct().ToArray();
        var existingCanonicalEntries = playlistIds.Length == 0
            ? []
            : await _dbContext.PlaylistEntries
                .Where(entry =>
                    playlistIds.Contains(entry.PlaylistId)
                    && entry.TrackId == trackId
                    && !entryIds.Contains(entry.Id) && !protectedEntryIds.Contains(entry.Id))
                .ToListAsync(cancellationToken);

        foreach (var playlistId in playlistIds)
        {
            var observationEntries = entries.Where(entry => entry.PlaylistId == playlistId);
            var playlist = await _dbContext.Playlists.SingleAsync(item => item.Id == playlistId, cancellationToken);
            playlist.SyncRevision++;
            if (playlist.SyncEnabled) playlist.NextSyncAt ??= DateTimeOffset.UtcNow.AddDays(1);
            if (playlist.AllowDuplicateTracks)
            {
                foreach (var entry in observationEntries) entry.TrackId = trackId;
                continue;
            }
            var canonicalEntries = existingCanonicalEntries.Where(entry => entry.PlaylistId == playlistId);
            var candidates = observationEntries
                .Concat(canonicalEntries)
                .OrderBy(entry => entry.Position)
                .ThenBy(entry => entry.Id)
                .ToList();
            var retainedEntry = candidates[0];
            retainedEntry.TrackId = trackId;

            _dbContext.PlaylistEntries.RemoveRange(candidates.Where(entry => entry.Id != retainedEntry.Id));
        }
    }
}
