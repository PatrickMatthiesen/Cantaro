using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services;

internal static class TrackMatchScorer
{
    public static TrackMatchScoredCandidate Score(
        Cantaro.Api.Models.TrackObservation observation,
        TrackMatchSearchCandidate candidate,
        TrackMatchingOptions options)
    {
        var parsedObservation = TrackMetadataParser.Parse(observation.Title, observation.Artist);
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

        decimal durationScore = 0m;
        if (observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue)
        {
            durationScore = TrackDurationSimilarity.Calculate(
                observation.DurationSeconds.Value,
                candidate.DurationSeconds.Value,
                options);
        }

        var semanticAdjustment = ComputeTitleSemanticAdjustment(parsedObservation, parsedCandidate, options);
        var score = (titleSimilarity * 0.55m) + (artistSimilarity * 0.30m) + (durationScore * 0.15m) + semanticAdjustment;
        var semanticExplanation = BuildSemanticExplanation(parsedObservation, parsedCandidate);
        var durationDifference = observation.DurationSeconds.HasValue && candidate.DurationSeconds.HasValue
            ? observation.DurationSeconds.Value - candidate.DurationSeconds.Value
            : (int?)null;
        var compatibleDuration = durationDifference.HasValue
            && Math.Abs(durationDifference.Value) <= options.AutoMatchDurationToleranceSeconds;
        var officialVideoPadding = string.Equals(observation.SourceType, "youtube", StringComparison.OrdinalIgnoreCase)
            && parsedObservation.PresentationMarkers.Contains("official-video", StringComparer.Ordinal)
            && titleSimilarity == 1m
            && exactCredits
            && semanticAdjustment >= 0m
            && durationDifference >= options.OfficialVideoPaddingMinSeconds
            && durationDifference <= options.OfficialVideoPaddingMaxSeconds
            && observation.DurationSeconds.HasValue
            && candidate.DurationSeconds.HasValue
            && candidate.DurationSeconds.Value > 0
            && (decimal)observation.DurationSeconds.Value / candidate.DurationSeconds.Value <= options.OfficialVideoPaddingMaxRatio;
        var isAutoMatchEligible = exactCredits && (compatibleDuration || officialVideoPadding);

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
            IsAutoMatchEligible = isAutoMatchEligible,
            AutoMatchEligibilityReason = isAutoMatchEligible
                ? officialVideoPadding ? "Exact credited official-video match with bounded source padding." : "Exact artist credits and compatible duration."
                : !exactCredits
                    ? "Automated matching requires exact artist credits."
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
