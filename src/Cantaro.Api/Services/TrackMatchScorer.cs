using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services;

internal static class TrackMatchScorer
{
    public static TrackMatchScoredCandidate Score(
        Cantaro.Api.Models.TrackObservation observation,
        TrackMatchSearchCandidate candidate,
        TrackMatchingOptions options)
        => TrackObservationParser.ParseSearchHypotheses(observation)
            .Select(parsed => Score(observation, candidate, options, parsed))
            .OrderByDescending(result => result.IsAutoMatchEligible)
            .ThenByDescending(result => result.Score)
            .First();

    private static TrackMatchScoredCandidate Score(
        Cantaro.Api.Models.TrackObservation observation,
        TrackMatchSearchCandidate candidate,
        TrackMatchingOptions options,
        ParsedTrackMetadata parsedObservation)
        => ParseCandidate(candidate)
            .Select(parsedCandidate => Score(observation, candidate, options, parsedObservation, parsedCandidate))
            .OrderByDescending(result => result.IsAutoMatchEligible)
            .ThenByDescending(result => result.Score).First();

    private static IReadOnlyList<ParsedTrackMetadata> ParseCandidate(TrackMatchSearchCandidate candidate)
    {
        if (candidate.CandidateSource != "youtube")
            return [TrackMetadataParser.Parse(candidate.Title, candidate.Artist, parseArtistFromTitle: false)];
        var rawMetadata = TrackMatchCandidateStoredMetadata.ReadProviderMetadata(candidate.RawMetadata);
        return TrackObservationParser.ParseSearchHypotheses(new Models.TrackObservation
        {
            SourceType = "youtube", ExternalId = candidate.ExternalId, Title = candidate.Title,
            Artist = candidate.Artist, DurationSeconds = candidate.DurationSeconds,
            RawMetadata = rawMetadata, MatchStatus = TrackMatchingStatuses.Pending
        });
    }

    private static TrackMatchScoredCandidate Score(
        Models.TrackObservation observation, TrackMatchSearchCandidate candidate,
        TrackMatchingOptions options, ParsedTrackMetadata parsedObservation, ParsedTrackMetadata parsedCandidate)
    {
        if (candidate.CandidateSource == "youtube")
        {
            candidate = new TrackMatchSearchCandidate
            {
                CandidateSource = candidate.CandidateSource, ExternalId = candidate.ExternalId,
                Title = parsedCandidate.DisplayTitle, Artist = parsedCandidate.DisplayArtist,
                ArtistCredits = parsedCandidate.ArtistCredits, DurationSeconds = candidate.DurationSeconds,
                Isrc = candidate.Isrc, MbidRecording = candidate.MbidRecording,
                ArtistMusicBrainzId = candidate.ArtistMusicBrainzId, ArtistSortName = candidate.ArtistSortName,
                Explanation = candidate.Explanation, RawMetadata = candidate.RawMetadata
            };
        }
        var titleSimilarity = BestSimilarity(candidate.Title, observation.Title, parsedObservation.DisplayTitle, parsedObservation.SearchTitle);
        var artistSimilarity = BestSimilarity(candidate.Artist, observation.Artist, parsedObservation.DisplayArtist, parsedObservation.SearchArtist);
        var exactCredits = TrackMetadataParser.HaveEquivalentArtistCredits(
            parsedObservation,
            parsedCandidate,
            candidate.ArtistCredits);
        if (exactCredits)
        {
            artistSimilarity = 1m;
        }

        var sameNamedMashup = TrackMatchCreditEvidence.IsSameNamedMashup(
            observation, candidate, parsedObservation, parsedCandidate);
        if (sameNamedMashup)
        {
            titleSimilarity = 1m;
            artistSimilarity = 1m;
        }
        var semanticAdjustment = sameNamedMashup ? 0m : ComputeTitleSemanticAdjustment(parsedObservation, parsedCandidate, options);
        var compatibleSemantics = sameNamedMashup || (!HaveDifferentMarkers(parsedObservation.VersionMarkers, parsedCandidate.VersionMarkers)
            && !HaveDifferentMarkers(parsedObservation.PlaybackModifiers, parsedCandidate.PlaybackModifiers));
        var semanticExplanation = sameNamedMashup ? string.Empty : BuildSemanticExplanation(parsedObservation, parsedCandidate);
        var durationDifference = observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue
            ? observation.DurationSeconds.Value - candidate.DurationSeconds.Value
            : (int?)null;
        var credibleCreditExpansion = HasCredibleCreditExpansion(
            parsedObservation, parsedCandidate, candidate, titleSimilarity);
        var exactTitle = sameNamedMashup || TrackTextNormalizer.AreEquivalentTitles(
            parsedObservation.SearchTitle, parsedCandidate.SearchTitle);
        var compatibleFeaturedCredits = TrackMatchCreditEvidence.HasCompatibleFeaturedCredits(
            observation, parsedObservation, candidate, exactTitle);
        var compatibleCredits = exactCredits || credibleCreditExpansion || compatibleFeaturedCredits || sameNamedMashup;
        if (compatibleFeaturedCredits) artistSimilarity = 1m;
        var conflictingCredits = parsedObservation.HasConflictingArtistEvidence || parsedCandidate.HasConflictingArtistEvidence;
        var strongIdentity = exactTitle && compatibleCredits
            && compatibleSemantics && !conflictingCredits;
        var durationScore = observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue
            ? TrackDurationSimilarity.Calculate(observation.DurationSeconds.Value, candidate.DurationSeconds.Value, options)
            : 0m;
        // For an otherwise exact identity, duration describes the provider's edit,
        // not whether this is the song. Keep it for diagnostics and tie-breaking.
        var score = strongIdentity
            ? ((titleSimilarity * 0.55m) + (artistSimilarity * 0.30m)) / 0.85m + semanticAdjustment
            : (titleSimilarity * 0.55m) + (artistSimilarity * 0.30m) + (durationScore * 0.15m) + semanticAdjustment;
        var compatibleDuration = durationDifference.HasValue
            && Math.Abs(durationDifference.Value) <= options.AutoMatchDurationToleranceSeconds;
        var isAutoMatchEligible = !conflictingCredits
            && compatibleCredits && compatibleSemantics
            && (strongIdentity || compatibleDuration);
        var sourceIsrc = TrackIdentityResolver.NormalizeIsrc(TrackObservationParser.ReadMetadata(observation)?.Isrc);
        var confirmedRecordingIdentity = sourceIsrc is not null
            && sourceIsrc == TrackIdentityResolver.NormalizeIsrc(candidate.Isrc)
            && strongIdentity;
        return new TrackMatchScoredCandidate
        {
            Candidate = candidate,
            ObservationTitle = observation.Title,
            ObservationArtist = observation.Artist,
            ObservationMetadata = parsedObservation,
            CandidateMetadata = parsedCandidate,
            TitleSimilarity = titleSimilarity,
            ArtistSimilarity = artistSimilarity,
            DurationScore = durationScore,
            SemanticAdjustment = semanticAdjustment,
            SemanticExplanation = semanticExplanation,
            Score = Math.Round(Math.Clamp(score, 0m, 1m), 3, MidpointRounding.AwayFromZero),
            ObservationDurationSeconds = observation.DurationSeconds,
            DurationDifferenceSeconds = durationDifference.HasValue ? Math.Abs(durationDifference.Value) : null,
            HasEquivalentArtistCredits = exactCredits,
            HasCompatibleSemantics = compatibleSemantics,
            IsAutoMatchEligible = isAutoMatchEligible,
            HasConfirmedRecordingIdentity = confirmedRecordingIdentity,
            AutoMatchEligibilityReason = conflictingCredits
                ? "The source has conflicting artist credits."
                : isAutoMatchEligible
                    ? strongIdentity
                        ? "Matching title, artist credits and version; duration is not required."
                        : "Compatible artist credits, version and duration support the title match."
                    : !compatibleCredits
                        ? "Automated matching requires all observed artist credits to match the recording."
                        : !compatibleSemantics
                            ? "Automated matching requires compatible version and playback markers."
                            : "The title is not an exact match and lacks supporting duration evidence."
        };
    }

    private static bool HasCredibleCreditExpansion(
        ParsedTrackMetadata observation,
        ParsedTrackMetadata parsedCandidate,
        TrackMatchSearchCandidate candidate,
        decimal titleSimilarity)
    {
        if (titleSimilarity != 1m
            || string.IsNullOrWhiteSpace(candidate.Isrc)
            || observation.ArtistCredits.Count == 0)
        {
            return false;
        }

        var candidateCredits = TrackMetadataParser.NormalizeArtistCredits(candidate.ArtistCredits);
        if (candidateCredits.Count == 0)
        {
            candidateCredits = parsedCandidate.ArtistCredits;
        }

        if (candidateCredits.Count < observation.ArtistCredits.Count) return false;
        var unmatched = observation.ArtistCredits
            .Where(credit => !candidateCredits.Contains(credit, StringComparer.Ordinal)).ToArray();
        if (unmatched.Length == 0) return true;

        // A parenthesized initial can be omitted from a catalog credit, for
        // example "(G)I-DLE" and "i-dle". Require corroborating exact credits;
        // this is not a general fuzzy artist-name comparison.
        if (unmatched.Length != 1 || observation.ArtistCredits.Count < 2) return false;
        foreach (System.Text.RegularExpressions.Match alias in System.Text.RegularExpressions.Regex.Matches(
            observation.DisplayArtist ?? "", @"(?<![\p{L}\p{Nd}])\(([\p{L}\p{Nd}])\)([\p{L}\p{Nd}][^,&]*)(?=,|&|$)"))
        {
            var full = TrackMetadataParser.NormalizeArtistCredits([alias.Value]).SingleOrDefault();
            var shortened = TrackTextNormalizer.Normalize(alias.Groups[2].Value);
            if (full == unmatched[0] && candidateCredits.Contains(shortened, StringComparer.Ordinal)) return true;
        }
        return false;
    }

    internal static decimal BestSimilarity(string? candidateValue, params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(candidateValue))
        {
            return 0m;
        }

        var best = 0m;
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            best = Math.Max(best, TrackTextNormalizer.CalculateSimilarity(value, candidateValue));
        }

        return best;
    }

    internal static decimal ComputeTitleSemanticAdjustment(ParsedTrackMetadata observation, ParsedTrackMetadata candidate, TrackMatchingOptions options)
    {
        if (HaveDifferentMarkers(observation.PlaybackModifiers, candidate.PlaybackModifiers))
        {
            return options.PlaybackModifierMismatchPenalty;
        }

        if (HaveDifferentMarkers(observation.VersionMarkers, candidate.VersionMarkers))
        {
            return options.VersionMismatchPenalty;
        }

        return observation.VersionMarkers.Count > 0 && HaveSameMarkers(observation.VersionMarkers, candidate.VersionMarkers)
            ? options.VersionMarkerMatchBonus
            : 0m;
    }

    internal static string BuildSemanticExplanation(ParsedTrackMetadata observation, ParsedTrackMetadata candidate)
    {
        if (HaveDifferentMarkers(observation.PlaybackModifiers, candidate.PlaybackModifiers))
        {
            return $"Playback modifiers differ: observation [{string.Join(", ", observation.PlaybackModifiers.DefaultIfEmpty("none"))}], candidate [{string.Join(", ", candidate.PlaybackModifiers.DefaultIfEmpty("none"))}].";
        }

        if (HaveDifferentMarkers(observation.VersionMarkers, candidate.VersionMarkers))
        {
            return $"Version markers differ: observation [{string.Join(", ", observation.VersionMarkers.DefaultIfEmpty("none"))}], candidate [{string.Join(", ", candidate.VersionMarkers.DefaultIfEmpty("none"))}].";
        }

        return string.Empty;
    }

    internal static bool HaveDifferentMarkers(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return !HaveSameMarkers(left, right) && (left.Count > 0 || right.Count > 0);
    }

    internal static bool HaveSameMarkers(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.OrderBy(marker => marker, StringComparer.Ordinal)
            .SequenceEqual(right.OrderBy(marker => marker, StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
