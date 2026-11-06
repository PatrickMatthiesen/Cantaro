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
                .ThenInclude(track => track!.SourceIds).ThenInclude(source => source.ProviderCatalogItem)
            .Include(item => item.Track).ThenInclude(track => track!.SourceIds)
                .ThenInclude(source => source.ProviderCatalogItem)
            .SingleOrDefaultAsync(item => item.Id == entry.Id, cancellationToken);
        if (stored?.Playlist?.UserId != account.UserId)
            return null;

        var exactObservation = stored.TrackObservation;
        if (exactObservation is not null && exactObservation.OwnerUserId != account.UserId) return null;
        if (exactObservation is not null && string.IsNullOrWhiteSpace(exactObservation.Title)) exactObservation = null;
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
                    Isrc: sourceMetadata?.Isrc, ArtistCredits: parsed.ArtistCredits, RawMetadata: exactObservation.RawMetadata,
                    OwnerUserId: account.UserId),
                cancellationToken);
            track = local?.Track;
            trackId = track?.Id;
        }
        if (trackId is not null && exactObservation is not null
            && (stored.TrackId != trackId || exactObservation.TrackId != trackId))
        {
            var sourceMapping = await dbContext.TrackSourceIds.Include(source => source.ProviderCatalogItem).SingleOrDefaultAsync(source =>
                source.SourceType == exactObservation.SourceType && source.ExternalId == exactObservation.ExternalId,
                cancellationToken);
            if (sourceMapping is not null && ProviderCatalogPolicy.IsVerified(sourceMapping, DateTimeOffset.UtcNow)
                && sourceMapping.TrackId != trackId) return null;
            var observation = await dbContext.TrackObservations.SingleAsync(item => item.Id == exactObservation.Id, cancellationToken);
            observation.TrackId = trackId;
            observation.MatchStatus = TrackMatchingStatuses.Matched;
            observation.UpdatedAt = DateTimeOffset.UtcNow;
            await reconcileObservationAsync(observation.Id, trackId.Value, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            stored.TrackId = trackId;
            stored.Track = track;
            exactObservation.TrackId = trackId;
        }
        var authorizedId = await PlaylistDestinationIdentityEvidence.FindAsync(
            dbContext, account, service, stored, cancellationToken);
        if (authorizedId is not null) return authorizedId;
        if (trackId is null && exactObservation is null) return null;

        var publicFacts = ProviderCatalogPolicy.SharedFacts(track, DateTimeOffset.UtcNow);
        var title = publicFacts?.Title ?? (exactObservation is not null ? exactObservation.Title : null);
        var artist = publicFacts?.Artist ?? (exactObservation is not null ? exactObservation.Artist : null);
        var duration = publicFacts?.DurationSeconds ?? exactObservation?.DurationSeconds;
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
        var candidates = await searchAsync(evidence, cancellationToken);
        var assessment = Assess(evidence,
            candidates.Where(candidate => candidate.CandidateSource == service).ToArray(), options);
        if (assessment.Accepted is not { } top) return null;

        var existing = await dbContext.TrackSourceIds.Include(source => source.ProviderCatalogItem).SingleOrDefaultAsync(
            source => source.SourceType == service && source.ExternalId == top.Candidate.ExternalId,
            cancellationToken);
        if (existing is not null && !ProviderCatalogPolicy.IsVerified(existing, DateTimeOffset.UtcNow)) existing = null;
        if (existing is not null && trackId is not null && existing.TrackId != trackId)
            return null;

        // Only the copied pre-search evidence can authorize artist enrichment.
        // The fresh mapping lookup above still rejects conflicting ownership.
        var materializingObservation = trackId is null;
        if (materializingObservation)
        {
            if (exactObservation is null) return null;
            if (existing is not null)
            {
                var trackedObservation = await dbContext.TrackObservations.SingleAsync(
                    observation => observation.Id == exactObservation.Id, cancellationToken);
                trackedObservation.TrackId = existing.TrackId;
                trackedObservation.MatchStatus = TrackMatchingStatuses.Matched;
                trackedObservation.UpdatedAt = DateTimeOffset.UtcNow;
                await reconcileObservationAsync(trackedObservation.Id,
                    existing.TrackId, cancellationToken);
                trackId = existing.TrackId;
            }
            else
            {
                trackId = await materializeObservationAsync(exactObservation.Id, top.Candidate, cancellationToken);
                // Materialization saves all accepted identities together. Re-read after it
                // rather than inserting a duplicate mapping with the same provider key.
                existing = await dbContext.TrackSourceIds.Include(source => source.ProviderCatalogItem).SingleOrDefaultAsync(
                    source => source.SourceType == service && source.ExternalId == top.Candidate.ExternalId,
                    cancellationToken);
                if (!ProviderCatalogPolicy.IsVerified(existing, DateTimeOffset.UtcNow)) existing = null;
            }
        }

        var resolvedTrackId = trackId ?? throw new InvalidOperationException("Matching did not create a canonical track.");
        if (existing is not null && existing.TrackId != resolvedTrackId) return null;
        // Search selections and manual destination choices stay scoped to this
        // playlist. They never create a reusable provider-to-recording assertion.
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
