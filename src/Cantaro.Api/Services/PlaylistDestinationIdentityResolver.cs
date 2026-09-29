using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

internal static class PlaylistDestinationIdentityResolver
{
    // Coalesce lookups for the same recording across playlists in this API process.
    // Fixed stripes keep the lock set bounded as the library grows.
    private static readonly SemaphoreSlim[] LookupGates = Enumerable.Range(0, 128)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public static async Task<string?> ResolveAsync(
        ApplicationDbContext dbContext,
        TrackMatchingOptions options,
        Func<Guid, TrackMatchSearchCandidate, CancellationToken, Task<Guid>> materializeObservationAsync,
        Func<Guid, Guid, CancellationToken, Task> reconcileObservationAsync,
        PlatformAccountContext account,
        string service,
        PlaylistEntry entry,
        Func<TrackObservation, CancellationToken, Task<IReadOnlyList<TrackMatchSearchCandidate>>> searchAsync,
        CancellationToken cancellationToken)
    {
        var identity = await dbContext.PlaylistEntries.AsNoTracking()
            .Where(item => item.Id == entry.Id && item.Playlist!.UserId == account.UserId)
            .Select(item => new { item.TrackId, item.TrackObservationId }).SingleOrDefaultAsync(cancellationToken);
        if (identity is null) return null;
        var key = identity.TrackObservationId ?? identity.TrackId ?? entry.Id;
        var gate = LookupGates[(int)((uint)HashCode.Combine(key, service) % (uint)LookupGates.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ResolveStoredAsync(dbContext, options, materializeObservationAsync,
                reconcileObservationAsync, account, service, entry, searchAsync, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static async Task<string?> ResolveStoredAsync(
        ApplicationDbContext dbContext,
        TrackMatchingOptions options,
        Func<Guid, TrackMatchSearchCandidate, CancellationToken, Task<Guid>> materializeObservationAsync,
        Func<Guid, Guid, CancellationToken, Task> reconcileObservationAsync,
        PlatformAccountContext account,
        string service,
        PlaylistEntry entry,
        Func<TrackObservation, CancellationToken, Task<IReadOnlyList<TrackMatchSearchCandidate>>> searchAsync,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.PlaylistEntries.AsNoTracking()
            .Include(item => item.Playlist)
            .Include(item => item.TrackObservation).ThenInclude(observation => observation!.Track)
                .ThenInclude(track => track!.SourceIds)
            .Include(item => item.Track).ThenInclude(track => track!.SourceIds)
            .SingleOrDefaultAsync(item => item.Id == entry.Id, cancellationToken);
        if (stored?.Playlist?.UserId != account.UserId)
            return null;

        var exactObservation = stored.TrackObservation;
        var track = stored.Track ?? exactObservation?.Track;
        var trackId = track?.Id;

        // A source item can already have been identified by another playlist or user,
        // even if this playlist entry has not yet been reconciled.
        if (track is null && exactObservation is not null)
        {
            var parsed = TrackObservationParser.Parse(exactObservation);
            var sourceMetadata = TrackObservationParser.ReadMetadata(exactObservation);
            var local = await new TrackIdentityResolver(dbContext, Options.Create(options)).ResolveExistingAsync(
                new TrackIdentityQuery(exactObservation.SourceType, exactObservation.ExternalId,
                    exactObservation.Title, exactObservation.Artist, exactObservation.DurationSeconds,
                    Isrc: sourceMetadata?.Isrc, ArtistCredits: parsed.ArtistCredits, RawMetadata: exactObservation.RawMetadata),
                cancellationToken);
            track = local?.Track;
            trackId = track?.Id;
        }
        if (trackId is not null && exactObservation is not null
            && (stored.TrackId != trackId || exactObservation.TrackId != trackId))
        {
            var sourceMapping = await dbContext.TrackSourceIds.SingleOrDefaultAsync(source =>
                source.SourceType == exactObservation.SourceType && source.ExternalId == exactObservation.ExternalId,
                cancellationToken);
            if (sourceMapping is not null && sourceMapping.TrackId != trackId) return null;
            var observation = await dbContext.TrackObservations.SingleAsync(item => item.Id == exactObservation.Id, cancellationToken);
            observation.TrackId = trackId;
            observation.MatchStatus = TrackMatchingStatuses.Matched;
            observation.UpdatedAt = DateTimeOffset.UtcNow;
            if (sourceMapping is null)
                dbContext.TrackSourceIds.Add(new TrackSourceId
                {
                    Id = Guid.NewGuid(), TrackId = trackId.Value, SourceType = observation.SourceType,
                    ExternalId = observation.ExternalId, LastVerifiedAt = DateTimeOffset.UtcNow
                });
            await reconcileObservationAsync(observation.Id, trackId.Value, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (exactObservation?.SourceType == service
            && (trackId is null || exactObservation.TrackId == trackId))
            return exactObservation.ExternalId;

        var known = trackId is null ? null : await dbContext.TrackSourceIds
            .Where(source => source.TrackId == trackId && source.SourceType == service)
            .OrderByDescending(source => source.Confidence)
            .ThenByDescending(source => source.LastVerifiedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (known is not null) return known.ExternalId;
        if (trackId is null && exactObservation is null) return null;

        TrackCanonicalMetadata? canonical = null;
        if (!string.IsNullOrWhiteSpace(track?.CanonicalMetadata))
        {
            try { canonical = JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata); }
            catch (JsonException) { return null; }
        }
        var title = canonical?.Title ?? track?.SearchTitle ?? exactObservation?.Title;
        var artist = canonical?.Artist ?? track?.SearchArtist ?? exactObservation?.Artist;
        var duration = canonical?.DurationSeconds ?? exactObservation?.DurationSeconds;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        var evidence = exactObservation ?? new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "cantaro", ExternalId = trackId!.Value.ToString(),
            Title = title, Artist = artist, DurationSeconds = duration,
            MatchStatus = TrackMatchingStatuses.Pending, TrackId = trackId
        };
        // This is an untracked observation. Enrich search evidence without changing
        // the imported title, duration, or version markers saved on the source.
        var metadata = TrackObservationParser.ReadMetadata(evidence) ?? new TrackObservationMetadata();
        metadata.Isrc = TrackIdentityResolver.NormalizeIsrc(track?.Isrc)
            ?? TrackIdentityResolver.NormalizeIsrc(metadata.Isrc);
        evidence.RawMetadata = JsonSerializer.Serialize(metadata);
        var candidates = await searchAsync(evidence, cancellationToken);
        var assessment = Assess(evidence,
            candidates.Where(candidate => candidate.CandidateSource == service).ToArray(), options);
        if (assessment.Accepted is not { } top) return null;

        var existing = await dbContext.TrackSourceIds.SingleOrDefaultAsync(
            source => source.SourceType == service && source.ExternalId == top.Candidate.ExternalId,
            cancellationToken);
        if (existing is not null && trackId is not null)
            return existing.TrackId == trackId ? existing.ExternalId : null;

        if (trackId is null)
        {
            if (exactObservation is null) return null;
            if (existing is not null)
            {
                var trackedObservation = await dbContext.TrackObservations.SingleAsync(
                    observation => observation.Id == exactObservation.Id, cancellationToken);
                trackedObservation.TrackId = existing.TrackId;
                trackedObservation.MatchStatus = TrackMatchingStatuses.Matched;
                trackedObservation.UpdatedAt = DateTimeOffset.UtcNow;
                if (!await dbContext.TrackSourceIds.AnyAsync(source => source.SourceType == trackedObservation.SourceType
                    && source.ExternalId == trackedObservation.ExternalId, cancellationToken))
                    dbContext.TrackSourceIds.Add(new TrackSourceId
                    {
                        Id = Guid.NewGuid(), TrackId = existing.TrackId, SourceType = trackedObservation.SourceType,
                        ExternalId = trackedObservation.ExternalId, LastVerifiedAt = DateTimeOffset.UtcNow
                    });
                await reconcileObservationAsync(trackedObservation.Id,
                    existing.TrackId, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return existing.ExternalId;
            }
            trackId = await materializeObservationAsync(exactObservation.Id, top.Candidate, cancellationToken);
            // Materialization saves all accepted identities together. Re-read after it
            // rather than inserting a duplicate mapping with the same provider key.
            existing = await dbContext.TrackSourceIds.SingleOrDefaultAsync(
                source => source.SourceType == service && source.ExternalId == top.Candidate.ExternalId,
                cancellationToken);
        }

        if (existing is not null) return existing.TrackId == trackId ? existing.ExternalId : null;
        dbContext.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = trackId.Value, SourceType = service,
            ExternalId = top.Candidate.ExternalId, Confidence = top.Score,
            LastVerifiedAt = DateTimeOffset.UtcNow,
            OriginMetadata = JsonSerializer.Serialize(new
            {
                method = "playlist_destination_lookup",
                title = top.Candidate.Title,
                artist = top.Candidate.Artist,
                durationSeconds = top.Candidate.DurationSeconds
            })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return top.Candidate.ExternalId;
    }

    internal static PlaylistDestinationMatchAssessment Assess(
        TrackObservation evidence, IReadOnlyList<TrackMatchSearchCandidate> candidates, TrackMatchingOptions options)
    {
        var assessment = TrackMatchDecisionEngine.Evaluate(evidence, candidates, options);
        var decision = assessment.Decision;
        return new PlaylistDestinationMatchAssessment(assessment.Ranked, decision.AcceptedCandidate,
            decision.AcceptedCandidate is not null ? "A matching recording was found."
                : assessment.Ranked.Count == 0 ? "No results were found on this platform."
                : DescribeCandidate(assessment.Ranked[0], options) ?? decision.ResolutionNotes);
    }

    internal static string? DescribeCandidate(TrackMatchScoredCandidate candidate, TrackMatchingOptions options)
    {
        if (candidate.ObservationMetadata.HasConflictingArtistEvidence || candidate.CandidateMetadata.HasConflictingArtistEvidence)
            return "The source has conflicting artist credits.";
        if (!candidate.HasCompatibleSemantics)
            return "This result has a different version or playback speed.";
        if (!candidate.IsAutoMatchEligible && !candidate.HasEquivalentArtistCredits
            && candidate.AutoMatchEligibilityReason.Contains("artist credits", StringComparison.OrdinalIgnoreCase))
        {
            var sourceCredits = candidate.ObservationMetadata.DisplayArtist ?? candidate.ObservationArtist;
            var resultCredits = candidate.Candidate.ArtistCredits.Count > 0
                ? string.Join(", ", candidate.Candidate.ArtistCredits)
                : candidate.Candidate.Artist ?? candidate.CandidateMetadata.DisplayArtist;
            return string.IsNullOrWhiteSpace(sourceCredits) || string.IsNullOrWhiteSpace(resultCredits)
                ? "The artist credits differ."
                : $"Source credits {sourceCredits}; this result credits {resultCredits}.";
        }
        if (candidate.Score < options.AutoMatchThreshold)
            return "Similarity is below the automatic matching threshold.";
        return null;
    }

    internal static IReadOnlyList<TrackMatchScoredCandidate> DistinctReviewCandidates(
        IReadOnlyList<TrackMatchScoredCandidate> ranked, TrackMatchingOptions options, int limit = 10)
    {
        return TrackMatchClusterer.BuildClusters(ranked, options.ClusterDurationToleranceSeconds)
            .Select(cluster => cluster.Representative).Take(limit).ToArray();
    }
}

internal sealed record PlaylistDestinationMatchAssessment(
    IReadOnlyList<TrackMatchScoredCandidate> Ranked,
    TrackMatchScoredCandidate? Accepted,
    string Reason);
