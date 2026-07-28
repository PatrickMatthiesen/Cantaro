using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

/// <summary>
/// Resolves Spotify catalog tracks directly to Cantaro identities without
/// requiring a secondary metadata search.
/// </summary>
public sealed class SpotifyTrackResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TrackIdentityResolver _identityResolver;

    public SpotifyTrackResolver(
        ApplicationDbContext dbContext,
        TrackIdentityResolver identityResolver)
    {
        _dbContext = dbContext;
        _identityResolver = identityResolver;
    }

    public SpotifyTrackResolver(ApplicationDbContext dbContext)
        : this(dbContext, new TrackIdentityResolver(dbContext))
    {
    }

    public async Task<Track> ResolveAsync(
        SpotifyTrackSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var identityMatch = await _identityResolver.ResolveExistingAsync(
            new TrackIdentityQuery(
                SpotifyService.ServiceName,
                snapshot.Id,
                snapshot.Name,
                snapshot.Artist,
                snapshot.DurationSeconds,
                snapshot.Isrc,
                ArtistCredits: snapshot.ArtistNames),
            cancellationToken);
        var sourceId = identityMatch?.ExactSourceMapping;
        var track = identityMatch?.Track;

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
            Isrc = TrackIdentityResolver.NormalizeIsrc(snapshot.Isrc),
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
            track.Isrc = TrackIdentityResolver.NormalizeIsrc(snapshot.Isrc);
        }

        if (string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            track.CanonicalMetadata = CreateCanonicalMetadata(snapshot);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.ImageUrl))
        {
            TrackArtworkUpdater.FillMissingCanonicalThumbnail(
                track,
                snapshot.ImageUrl);
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
            albumUrl = snapshot.AlbumUrl,
            imageUrl = snapshot.ImageUrl
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
            ThumbnailUrl = snapshot.ImageUrl,
            DurationSeconds = snapshot.DurationSeconds
        });

}
