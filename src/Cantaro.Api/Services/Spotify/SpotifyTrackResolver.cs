using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

/// <summary>Reuses public recording identities and keeps account snapshots on their owner's observations.</summary>
public sealed class SpotifyTrackResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TrackIdentityResolver _identityResolver;

    public SpotifyTrackResolver(ApplicationDbContext dbContext, TrackIdentityResolver identityResolver)
    {
        _dbContext = dbContext;
        _identityResolver = identityResolver;
    }

    public SpotifyTrackResolver(ApplicationDbContext dbContext) : this(dbContext, new TrackIdentityResolver(dbContext)) { }

    public async Task<Track> ResolveAsync(SpotifyTrackSnapshot snapshot, DateTimeOffset now,
        CancellationToken cancellationToken, int? ownerUserId = null)
    {
        var associations = new ProviderRecordingAssociations(_dbContext);
        var verified = await associations.FindUsableAsync(SpotifyService.ServiceName, snapshot.Id, cancellationToken);
        if (verified is not null)
            return await LoadTrackAsync(verified.TrackId, cancellationToken);

        var candidate = new TrackMatchSearchCandidate { CandidateSource = SpotifyService.ServiceName,
            ExternalId = snapshot.Id, Title = snapshot.Name, ProviderCatalogItemId = snapshot.ProviderCatalogItemId,
            ProviderCatalogRevision = snapshot.ProviderCatalogRevision };
        var publicItem = await associations.LoadCandidateAsync(candidate, cancellationToken);
        if (publicItem is not null && await associations.RevalidateExactAsync(publicItem, cancellationToken) is { } revalidated)
            return revalidated;

        var existing = await _identityResolver.ResolveExistingAsync(new TrackIdentityQuery(
            SpotifyService.ServiceName, snapshot.Id, snapshot.Name, snapshot.Artist,
            snapshot.DurationSeconds, snapshot.Isrc, ArtistCredits: snapshot.ArtistNames,
            OwnerUserId: ownerUserId), cancellationToken);
        var track = existing?.Track;
        if (track is null)
        {
            var song = new Song { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            track = new Track { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now };
            _dbContext.AddRange(song, track, new SongTrack { SongId = song.Id, Song = song, TrackId = track.Id, Track = track });
        }

        // References are attached only at an application-token acquisition boundary.
        // Reloading validates their key, revision and lease. Account response fields
        // do not become public metadata.
        if (publicItem is not null)
        {
            var independent = ProviderCatalogPolicy.SharedFacts(track, now);
            // The public provider response establishes the origin recording,
            // rather than copying the importing account's fields or credits.
            await associations.TryAssociateAsync(track, publicItem, independent,
                providerOrigin: existing is null, cancellationToken);
        }
        return track;
    }

    private async Task<Track> LoadTrackAsync(Guid id, CancellationToken ct) =>
        _dbContext.Tracks.Local.FirstOrDefault(track => track.Id == id)
        ?? await _dbContext.Tracks.Include(track => track.SourceIds).ThenInclude(source => source.ProviderCatalogItem)
            .Include(track => track.ArtistCredits).SingleAsync(track => track.Id == id, ct);
}
