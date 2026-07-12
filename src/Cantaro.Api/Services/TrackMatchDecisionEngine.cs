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
                ResolutionNotes = $"Automatically resolved by the matcher. Top cluster scored {topCandidate.Score:P0} with a {margin:P0} lead over the next distinct cluster.",
                AcceptedCandidate = topCandidate,
                TopScore = topCandidate.Score,
                SecondDistinctScore = secondDistinctScore,
                DecisionReason = $"Top cluster {topCandidate.Score:P0}; runner-up {secondDistinctScore:P0}; margin {margin:P0}."
            };
        }

        if (meetsAutoMatchThreshold && meetsAutoMatchMargin && !topCandidate.IsAutoMatchEligible)
        {
            var evidenceReason = $"Top cluster {topCandidate.Score:P0} meets the required {autoMatchThreshold:P0} score and its {margin:P0} lead meets the required {autoMatchMargin:P0} margin, but eligibility requirements are not met: {topCandidate.AutoMatchEligibilityReason.TrimEnd('.')}";

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
            autoMatchFailures.Add($"top cluster score {topCandidate.Score:P0} is below the required {autoMatchThreshold:P0}");
        }

        if (!meetsAutoMatchMargin)
        {
            autoMatchFailures.Add($"margin {margin:P0} is below the required {autoMatchMargin:P0}");
        }

        var failureReason = string.Join("; ", autoMatchFailures) + ".";
        var scoreSummary = $"Top cluster {topCandidate.Score:P0}; runner-up {secondDistinctScore:P0}; margin {margin:P0}.";
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
}
