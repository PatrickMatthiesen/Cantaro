using System.Globalization;

namespace Cantaro.Api.Services;

internal static class TrackMatchDecisionEngine
{
    public static TrackMatchDecision Decide(
        IReadOnlyList<TrackMatchScoredCandidate> rankedCandidates,
        IReadOnlyList<TrackMatchCluster> clusters,
        decimal autoMatchThreshold,
        decimal ambiguousThreshold,
        decimal autoMatchMargin)
    {
        if (rankedCandidates.Count == 0)
        {
            return new TrackMatchDecision
            {
                MatchStatus = TrackMatchingStatuses.NoMatch,
                ResolutionNotes = "No credible candidate was found.",
                AcceptedCandidate = null,
                TopScore = 0m,
                SecondDistinctScore = 0m,
                DecisionReason = "No candidate reached the minimum credibility floor."
            };
        }

        var topCandidate = rankedCandidates[0];
        var secondDistinctScore = clusters.Count > 1 ? clusters[1].Representative.Score : 0m;
        var margin = topCandidate.Score - secondDistinctScore;

        var meetsAutoMatchThreshold = topCandidate.Score >= autoMatchThreshold;
        var meetsAutoMatchMargin = margin >= autoMatchMargin;

        if (meetsAutoMatchThreshold && meetsAutoMatchMargin && topCandidate.IsAutoMatchEligible)
        {
            return new TrackMatchDecision
            {
                MatchStatus = TrackMatchingStatuses.Matched,
                ResolutionNotes = $"Automatically resolved by the matcher. Top cluster scored {FormatPercentage(topCandidate.Score)} with a {FormatPercentage(margin)} lead over the next distinct cluster.",
                AcceptedCandidate = topCandidate,
                TopScore = topCandidate.Score,
                SecondDistinctScore = secondDistinctScore,
                DecisionReason = $"Top cluster {FormatPercentage(topCandidate.Score)}; runner-up {FormatPercentage(secondDistinctScore)}; margin {FormatPercentage(margin)}."
            };
        }

        if (meetsAutoMatchThreshold && meetsAutoMatchMargin && !topCandidate.IsAutoMatchEligible)
        {
            var evidenceReason = $"Top cluster {FormatPercentage(topCandidate.Score)} meets the required {FormatPercentage(autoMatchThreshold)} score and its {FormatPercentage(margin)} lead meets the required {FormatPercentage(autoMatchMargin)} margin, but eligibility requirements are not met: {topCandidate.AutoMatchEligibilityReason.TrimEnd('.')}";

            return new TrackMatchDecision
            {
                MatchStatus = TrackMatchingStatuses.Ambiguous,
                ResolutionNotes = $"Manual review is required. {evidenceReason}",
                AcceptedCandidate = null,
                TopScore = topCandidate.Score,
                SecondDistinctScore = secondDistinctScore,
                DecisionReason = $"Distinct clusters: {clusters.Count}. {evidenceReason}"
            };
        }

        var matchStatus = topCandidate.Score >= ambiguousThreshold
            ? TrackMatchingStatuses.Ambiguous
            : TrackMatchingStatuses.NoMatch;

        var autoMatchFailures = new List<string>();
        if (!meetsAutoMatchThreshold)
        {
            autoMatchFailures.Add($"top cluster score {FormatPercentage(topCandidate.Score)} is below the required {FormatPercentage(autoMatchThreshold)}");
        }

        if (!meetsAutoMatchMargin)
        {
            autoMatchFailures.Add($"margin {FormatPercentage(margin)} is below the required {FormatPercentage(autoMatchMargin)}");
        }

        var failureReason = string.Join("; ", autoMatchFailures) + ".";
        var scoreSummary = $"Top cluster {FormatPercentage(topCandidate.Score)}; runner-up {FormatPercentage(secondDistinctScore)}; margin {FormatPercentage(margin)}.";
        var statusReason = matchStatus == TrackMatchingStatuses.Ambiguous
            ? $"Multiple plausible candidates require review because {failureReason} {scoreSummary}"
            : $"Candidates were found, but automatic resolution was not possible because {failureReason} {scoreSummary}";

        return new TrackMatchDecision
        {
            MatchStatus = matchStatus,
            ResolutionNotes = statusReason,
            AcceptedCandidate = null,
            TopScore = topCandidate.Score,
            SecondDistinctScore = secondDistinctScore,
            DecisionReason = $"Distinct clusters: {clusters.Count}. {failureReason} {scoreSummary}"
        };
    }

    private static string FormatPercentage(decimal value) =>
        $"{(value * 100m).ToString("0", CultureInfo.InvariantCulture)}%";
}
