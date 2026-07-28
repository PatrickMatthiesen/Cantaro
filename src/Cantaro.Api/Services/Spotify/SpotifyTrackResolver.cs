using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

/// <summary>
/// Resolves Spotify catalog tracks directly to Cantaro identities without
/// requiring a secondary metadata search.
/// </summary>
public sealed class SpotifyTrackResolver(ApplicationDbContext dbContext)
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<Track> ResolveAsync(
        SpotifyTrackSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sourceId = _dbContext.TrackSourceIds.Local.FirstOrDefault(IsSpotifySource)
            ?? await _dbContext.TrackSourceIds.SingleOrDefaultAsync(
                candidate => candidate.SourceType == SpotifyService.ServiceName
                    && candidate.ExternalId == snapshot.Id,
                cancellationToken);

        Track? track = null;
        if (sourceId != null)
        {
            track = await LoadTrackAsync(sourceId.TrackId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Spotify source {snapshot.Id} references missing track {sourceId.TrackId}.");
        }
        else
        {
            track = await FindUniqueTrackByIsrcAsync(snapshot.Isrc, cancellationToken);
        }

        if (track == null)
        {
            track = CreateTrack(snapshot, now);
        }
        else
        {
            UpdateExistingTrack(track, snapshot, now);
        }

        EnsureArtistCredits(track, snapshot, now);
        EnsureSourceMapping(track, sourceId, snapshot, now);
        return track;

        bool IsSpotifySource(TrackSourceId candidate) =>
            candidate.SourceType == SpotifyService.ServiceName
            && candidate.ExternalId == snapshot.Id;
    }

    private async Task<Track?> FindUniqueTrackByIsrcAsync(
        string? rawIsrc,
        CancellationToken cancellationToken)
    {
        var isrc = NormalizeIsrc(rawIsrc);
        if (isrc == null)
        {
            return null;
        }

        var candidateIds = _dbContext.Tracks.Local
            .Where(track => NormalizeIsrc(track.Isrc) == isrc)
            .Select(track => track.Id)
            .ToHashSet();

        if (candidateIds.Count < 2)
        {
            var storedIds = await _dbContext.Tracks
                .Where(track => track.Isrc == isrc || track.Isrc == rawIsrc)
                .Select(track => track.Id)
                .Take(2)
                .ToListAsync(cancellationToken);
            candidateIds.UnionWith(storedIds);
        }

        return candidateIds.Count == 1
            ? await LoadTrackAsync(candidateIds.Single(), cancellationToken)
            : null;
    }

    private async Task<Track?> LoadTrackAsync(Guid trackId, CancellationToken cancellationToken)
    {
        var localTrack = _dbContext.Tracks.Local.FirstOrDefault(candidate => candidate.Id == trackId);
        if (localTrack != null)
        {
            var credits = _dbContext.Entry(localTrack).Collection(track => track.ArtistCredits);
            if (_dbContext.Entry(localTrack).State != EntityState.Added && !credits.IsLoaded)
            {
                await credits.Query()
                    .Include(credit => credit.Artist)
                    .LoadAsync(cancellationToken);
            }

            return localTrack;
        }

        return await _dbContext.Tracks
            .Include(track => track.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .SingleOrDefaultAsync(track => track.Id == trackId, cancellationToken);
    }

    private Track CreateTrack(SpotifyTrackSnapshot snapshot, DateTimeOffset now)
    {
        var song = new Song
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
        var track = new Track
        {
            Id = Guid.NewGuid(),
            Isrc = NormalizeIsrc(snapshot.Isrc),
            CanonicalMetadata = CreateCanonicalMetadata(snapshot),
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new SongTrack
        {
            SongId = song.Id,
            Song = song,
            TrackId = track.Id,
            Track = track
        };

        _dbContext.AddRange(song, track, membership);
        return track;
    }

    private static void UpdateExistingTrack(
        Track track,
        SpotifyTrackSnapshot snapshot,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(track.Isrc))
        {
            track.Isrc = NormalizeIsrc(snapshot.Isrc);
        }

        if (string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            track.CanonicalMetadata = CreateCanonicalMetadata(snapshot);
        }

        track.UpdatedAt = now;
    }

    private void EnsureArtistCredits(
        Track track,
        SpotifyTrackSnapshot snapshot,
        DateTimeOffset now)
    {
        if (track.ArtistCredits.Count > 0)
        {
            return;
        }

        var artistNames = snapshot.ArtistNames.Count > 0
            ? snapshot.ArtistNames
            : [snapshot.Artist];

        for (var position = 0; position < artistNames.Count; position++)
        {
            var creditedName = artistNames[position].Trim();
            if (creditedName.Length == 0)
            {
                continue;
            }

            // Names are not stable artist identities, so a provider ID is
            // required before separate artist rows may be reconciled.
            var artist = new Artist
            {
                Id = Guid.NewGuid(),
                Name = creditedName,
                CreatedAt = now,
                UpdatedAt = now
            };
            var credit = new TrackArtistCredit
            {
                Id = Guid.NewGuid(),
                TrackId = track.Id,
                ArtistId = artist.Id,
                Artist = artist,
                Role = TrackArtistRole.Primary,
                Position = position,
                CreditedName = creditedName
            };

            track.ArtistCredits.Add(credit);
            _dbContext.AddRange(artist, credit);
        }
    }

    private void EnsureSourceMapping(
        Track track,
        TrackSourceId? sourceId,
        SpotifyTrackSnapshot snapshot,
        DateTimeOffset now)
    {
        sourceId ??= new TrackSourceId
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            SourceType = SpotifyService.ServiceName,
            ExternalId = snapshot.Id
        };

        sourceId.TrackId = track.Id;
        sourceId.Confidence = 1m;
        sourceId.IsOfficial = true;
        sourceId.LastVerifiedAt = now;
        sourceId.OriginMetadata = JsonSerializer.Serialize(new
        {
            externalUrl = snapshot.ExternalUrl,
            album = snapshot.AlbumName,
            albumUrl = snapshot.AlbumUrl
        });

        if (_dbContext.Entry(sourceId).State == EntityState.Detached)
        {
            _dbContext.TrackSourceIds.Add(sourceId);
        }
    }

    private static string CreateCanonicalMetadata(SpotifyTrackSnapshot snapshot) =>
        JsonSerializer.Serialize(new TrackCanonicalMetadata
        {
            Title = snapshot.Name,
            Artist = snapshot.Artist,
            Albums = string.IsNullOrWhiteSpace(snapshot.AlbumName) ? [] : [snapshot.AlbumName],
            DurationSeconds = snapshot.DurationSeconds
        });

    private static string? NormalizeIsrc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
