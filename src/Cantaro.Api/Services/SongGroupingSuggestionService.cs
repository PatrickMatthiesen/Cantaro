using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class SongGroupingSuggestionService(ApplicationDbContext dbContext)
{
    private const TrackVersionFlags UnsafeAutomaticGroupingFlags =
        TrackVersionFlags.Cover | TrackVersionFlags.Mashup | TrackVersionFlags.Medley;

    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<SongGroupingSuggestion>> GenerateAsync(
        CancellationToken cancellationToken)
    {
        var tracks = (await _dbContext.Tracks
            .AsNoTracking()
            .Include(track => track.SongMemberships)
            .Include(track => track.ArtistCredits)
                .ThenInclude(credit => credit.Artist)
            .ToListAsync(cancellationToken))
            .OrderBy(track => track.CreatedAt)
            .ThenBy(track => track.Id)
            .ToList();
        var songTrackCounts = await _dbContext.SongTracks
            .GroupBy(membership => membership.SongId)
            .Select(group => new { SongId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.SongId, item => item.Count, cancellationToken);
        var creditedSongIds = await _dbContext.SongCredits
            .Select(credit => credit.SongId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        var existingKeys = await _dbContext.SongGroupingSuggestions
            .Select(suggestion => new
            {
                suggestion.CandidateTrackId,
                suggestion.TargetSongId
            })
            .ToListAsync(cancellationToken);
        var knownKeys = existingKeys
            .Select(item => (item.CandidateTrackId, item.TargetSongId))
            .ToHashSet();
        var groupingCandidates = new List<TrackGroupingCandidate>();
        foreach (var track in tracks)
        {
            if (!TryDescribe(track, out var description))
            {
                continue;
            }

            groupingCandidates.Add(new TrackGroupingCandidate(
                track,
                description,
                track.SongMemberships.Single().SongId));
        }

        var validGroupedAnchorSongIds = groupingCandidates
            .GroupBy(candidate => candidate.SourceSongId)
            .Where(group =>
                songTrackCounts.TryGetValue(group.Key, out var trackCount)
                && trackCount > 1
                && group.Count() == trackCount
                && group.Select(CreateGroupingKey).Distinct().Count() == 1)
            .Select(group => group.Key)
            .ToHashSet();

        var created = new List<SongGroupingSuggestion>();
        foreach (var cluster in groupingCandidates.GroupBy(CreateGroupingKey))
        {
            var anchor = cluster
                .Where(candidate =>
                    validGroupedAnchorSongIds.Contains(candidate.SourceSongId)
                    || IsGeneratedSingletonSong(
                        candidate.SourceSongId,
                        songTrackCounts,
                        creditedSongIds))
                .OrderBy(candidate =>
                    validGroupedAnchorSongIds.Contains(candidate.SourceSongId) ? 0 : 1)
                .ThenBy(candidate => HasVersionIdentity(candidate) ? 1 : 0)
                .ThenBy(candidate => candidate.Track.CreatedAt)
                .ThenBy(candidate => candidate.Track.Id)
                .FirstOrDefault();
            if (anchor == null)
            {
                continue;
            }

            foreach (var candidate in cluster.Where(candidate =>
                         candidate.Track.Id != anchor.Track.Id
                         && IsGeneratedSingletonSong(
                             candidate.SourceSongId,
                             songTrackCounts,
                             creditedSongIds)))
            {
                var targetMembers = cluster.Where(member =>
                    member.SourceSongId == anchor.SourceSongId);
                if (!targetMembers.All(member =>
                        CanSuggest(candidate.Description, member.Description)
                        && HasDistinctVersionEvidence(candidate, member)))
                {
                    continue;
                }

                var sourceSongId = candidate.SourceSongId;
                var targetSongId = anchor.SourceSongId;
                if (sourceSongId == targetSongId
                    || !knownKeys.Add((candidate.Track.Id, targetSongId)))
                {
                    continue;
                }

                var suggestion = new SongGroupingSuggestion
                {
                    Id = Guid.NewGuid(),
                    CandidateTrackId = candidate.Track.Id,
                    SourceSongId = sourceSongId,
                    TargetSongId = targetSongId,
                    AnchorTrackId = anchor.Track.Id,
                    Kind = SongGroupingSuggestionKinds.SameComposition,
                    Confidence = 0.9m,
                    EvidenceJson = JsonSerializer.Serialize(new
                    {
                        reason = "Same base title and artist credits with distinct version evidence.",
                        candidate = Evidence(candidate.Track, candidate.Description),
                        anchor = Evidence(anchor.Track, anchor.Description)
                    }),
                    Status = SongGroupingSuggestionStatuses.Pending,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                created.Add(suggestion);
                _dbContext.SongGroupingSuggestions.Add(suggestion);
            }
        }

        if (created.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    public async Task<SongGroupingSuggestion> ReviewAsync(
        Guid suggestionId,
        int reviewerUserId,
        bool accept,
        CancellationToken cancellationToken)
    {
        var suggestion = await _dbContext.SongGroupingSuggestions
            .SingleOrDefaultAsync(item => item.Id == suggestionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Song grouping suggestion {suggestionId} was not found.");
        if (suggestion.Status != SongGroupingSuggestionStatuses.Pending)
        {
            throw new InvalidOperationException(
                $"Song grouping suggestion {suggestionId} has already been reviewed.");
        }

        if (accept)
        {
            var sourceMembership = await _dbContext.SongTracks.SingleOrDefaultAsync(
                membership => membership.TrackId == suggestion.CandidateTrackId
                    && membership.SongId == suggestion.SourceSongId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The candidate Track no longer has its proposed source Song membership.");
            var sourceSongTrackCount = await _dbContext.SongTracks.CountAsync(
                membership => membership.SongId == suggestion.SourceSongId,
                cancellationToken);
            var sourceSongHasCredits = await _dbContext.SongCredits.AnyAsync(
                credit => credit.SongId == suggestion.SourceSongId,
                cancellationToken);
            if (sourceSongTrackCount != 1 || sourceSongHasCredits)
            {
                throw new InvalidOperationException(
                    "Only generated singleton Songs without composition credits can be regrouped.");
            }
            var hasOtherMemberships = await _dbContext.SongTracks.AnyAsync(
                membership => membership.TrackId == suggestion.CandidateTrackId
                    && membership.SongId != suggestion.SourceSongId,
                cancellationToken);
            if (hasOtherMemberships)
            {
                throw new InvalidOperationException(
                    "Tracks with multiple Song memberships require a separate review workflow.");
            }

            var anchorMemberships = await _dbContext.SongTracks
                .Where(membership => membership.TrackId == suggestion.AnchorTrackId)
                .Select(membership => membership.SongId)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (anchorMemberships.Count != 1)
            {
                throw new InvalidOperationException(
                    "The anchor Track no longer has exactly one Song membership.");
            }

            var currentTargetSongId = anchorMemberships[0];
            var targetExists = await _dbContext.Songs.AnyAsync(
                song => song.Id == currentTargetSongId,
                cancellationToken);
            if (!targetExists)
            {
                throw new InvalidOperationException(
                    "The proposed target Song no longer exists.");
            }

            suggestion.TargetSongId = currentTargetSongId;
            _dbContext.SongTracks.Remove(sourceMembership);
            _dbContext.SongTracks.Add(new SongTrack
            {
                SongId = currentTargetSongId,
                TrackId = suggestion.CandidateTrackId
            });
        }

        var now = DateTimeOffset.UtcNow;
        suggestion.Status = accept
            ? SongGroupingSuggestionStatuses.Accepted
            : SongGroupingSuggestionStatuses.Rejected;
        suggestion.ReviewedAt = now;
        suggestion.ReviewedByUserId = reviewerUserId;
        suggestion.AppliedAt = accept ? now : null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return suggestion;
    }

    private static bool TryDescribe(
        Track track,
        out TrackGroupingDescription description)
    {
        description = default!;
        if (track.SongMemberships.Count != 1
            || (track.VersionFlags & UnsafeAutomaticGroupingFlags) != 0
            || string.IsNullOrWhiteSpace(track.CanonicalMetadata))
        {
            return false;
        }

        TrackCanonicalMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(
                track.CanonicalMetadata);
        }
        catch (JsonException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(metadata?.Title))
        {
            return false;
        }

        var parsed = TrackMetadataParser.Parse(metadata.Title, metadata.Artist);
        var credits = TrackMetadataParser.NormalizeArtistCredits(
            track.ArtistCredits.Count > 0
                ? track.ArtistCredits
                    .OrderBy(credit => credit.Position)
                    .Select(credit => credit.CreditedName ?? credit.Artist?.Name)
                : parsed.ArtistCredits.Cast<string?>());
        if (credits.Count == 0)
        {
            return false;
        }

        description = new TrackGroupingDescription(
            parsed,
            credits,
            TrackIdentityResolver.NormalizeIsrc(track.Isrc),
            NormalizeStableId(track.MbidRecording));
        return true;
    }

    private static bool IsGeneratedSingletonSong(
        Guid songId,
        IReadOnlyDictionary<Guid, int> songTrackCounts,
        IReadOnlySet<Guid> creditedSongIds) =>
        songTrackCounts.TryGetValue(songId, out var count)
        && count == 1
        && !creditedSongIds.Contains(songId);

    private static bool CanSuggest(
        TrackGroupingDescription candidate,
        TrackGroupingDescription anchor)
    {
        if (!TrackTextNormalizer.AreEquivalentTitles(
                candidate.Metadata.SearchTitle,
                anchor.Metadata.SearchTitle)
            || !candidate.ArtistCredits.SequenceEqual(
                anchor.ArtistCredits,
                StringComparer.Ordinal))
        {
            return false;
        }

        if (candidate.Isrc != null
            && string.Equals(candidate.Isrc, anchor.Isrc, StringComparison.Ordinal))
        {
            return false;
        }

        return candidate.MusicBrainzRecordingId == null
            || anchor.MusicBrainzRecordingId == null
            || !string.Equals(
                candidate.MusicBrainzRecordingId,
                anchor.MusicBrainzRecordingId,
                StringComparison.Ordinal);
    }

    private static bool HasVersionIdentity(TrackGroupingCandidate candidate) =>
        candidate.Description.VersionMarkers.Count > 0
        || candidate.Track.VersionFlags != TrackVersionFlags.None;

    private static bool HasDistinctVersionEvidence(
        TrackGroupingCandidate candidate,
        TrackGroupingCandidate anchor) =>
        (HasVersionIdentity(candidate) || HasVersionIdentity(anchor))
        && (!candidate.Description.VersionMarkers.SequenceEqual(
                anchor.Description.VersionMarkers,
                StringComparer.Ordinal)
            || candidate.Track.VersionFlags != anchor.Track.VersionFlags);

    private static object Evidence(
        Track track,
        TrackGroupingDescription description) => new
    {
        trackId = track.Id,
        title = description.Metadata.DisplayTitle,
        artist = description.Metadata.DisplayArtist,
        artistCredits = description.ArtistCredits,
        isrc = description.Isrc,
        musicBrainzRecordingId = description.MusicBrainzRecordingId,
        versionMarkers = description.Metadata.VersionMarkers,
        versionFlags = track.VersionFlags.ToString()
    };

    private static string? NormalizeStableId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToLowerInvariant();

    private static TrackGroupingKey CreateGroupingKey(
        TrackGroupingCandidate candidate) =>
        new(
            TrackTextNormalizer.Normalize(
                candidate.Description.Metadata.SearchTitle),
            string.Join('\u001F', candidate.Description.ArtistCredits));

    private sealed record TrackGroupingDescription(
        ParsedTrackMetadata Metadata,
        IReadOnlyList<string> ArtistCredits,
        string? Isrc,
        string? MusicBrainzRecordingId)
    {
        public IReadOnlyList<string> VersionMarkers => Metadata.VersionMarkers;
    }

    private sealed record TrackGroupingCandidate(
        Track Track,
        TrackGroupingDescription Description,
        Guid SourceSongId);

    private sealed record TrackGroupingKey(
        string Title,
        string ArtistCredits);
}
