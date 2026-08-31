using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services;

internal static class TrackMatchScorer
{
    public static TrackMatchScoredCandidate Score(
        Cantaro.Api.Models.TrackObservation observation,
        TrackMatchSearchCandidate candidate,
        TrackMatchingOptions options)
    {
        var parsedObservation = TrackObservationParser.Parse(observation);
        var parsedCandidate = TrackMetadataParser.Parse(candidate.Title, candidate.Artist);
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

        var semanticAdjustment = ComputeTitleSemanticAdjustment(parsedObservation, parsedCandidate, options);
        var compatibleSemantics = !HaveDifferentMarkers(parsedObservation.VersionMarkers, parsedCandidate.VersionMarkers)
            && !HaveDifferentMarkers(parsedObservation.PlaybackModifiers, parsedCandidate.PlaybackModifiers);
        var semanticExplanation = BuildSemanticExplanation(parsedObservation, parsedCandidate);
        var durationDifference = observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue
            ? observation.DurationSeconds.Value - candidate.DurationSeconds.Value
            : (int?)null;
        var durationRatio = observation.DurationSeconds.HasValue
            && candidate.DurationSeconds is > 0
                ? (decimal)observation.DurationSeconds.Value / candidate.DurationSeconds.Value
                : (decimal?)null;
        var exactYouTubeIdentity = string.Equals(observation.SourceType, "youtube", StringComparison.OrdinalIgnoreCase)
            && titleSimilarity == 1m
            && exactCredits
            && semanticAdjustment >= 0m;
        var useYouTubePaddingScore = exactYouTubeIdentity
            && durationDifference > 0;
        var unmarkedYouTubePadding = exactYouTubeIdentity
            && durationDifference > options.AutoMatchDurationToleranceSeconds
            && durationDifference <= options.YouTubeUnmarkedPaddingMaxSeconds
            && durationRatio <= options.YouTubeUnmarkedPaddingMaxRatio;
        var officialVideoPadding = exactYouTubeIdentity
            && parsedObservation.PresentationMarkers.Contains("official-video", StringComparer.Ordinal)
            && durationDifference >= options.OfficialVideoPaddingMinSeconds
            && durationDifference <= options.OfficialVideoPaddingMaxSeconds
            && durationRatio <= options.OfficialVideoPaddingMaxRatio;

        decimal durationScore = 0m;
        if (observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue)
        {
            durationScore = useYouTubePaddingScore
                ? TrackDurationSimilarity.CalculateWithObservationPadding(
                    observation.DurationSeconds.Value,
                    candidate.DurationSeconds.Value,
                    options)
                : TrackDurationSimilarity.Calculate(
                    observation.DurationSeconds.Value,
                    candidate.DurationSeconds.Value,
                    options);
        }

        var score = (titleSimilarity * 0.55m) + (artistSimilarity * 0.30m) + (durationScore * 0.15m) + semanticAdjustment;
        var compatibleDuration = durationDifference.HasValue
            && Math.Abs(durationDifference.Value) <= options.AutoMatchDurationToleranceSeconds;
        var isAutoMatchEligible = exactCredits
            && compatibleSemantics
            && (compatibleDuration || unmarkedYouTubePadding || officialVideoPadding);

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
            AutoMatchEligibilityReason = isAutoMatchEligible
                ? officialVideoPadding
                    ? "Exact credited official-video match with bounded source padding."
                    : unmarkedYouTubePadding
                        ? "Exact credited YouTube match with bounded source padding."
                        : "Exact artist credits and compatible duration."
                : !exactCredits
                    ? "Automated matching requires exact artist credits."
                    : !compatibleSemantics
                        ? "Automated matching requires compatible version and playback markers."
                    : "Automated matching requires compatible duration evidence."
        };
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
