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

        if (topCandidate.Score >= autoMatchThreshold && margin >= autoMatchMargin)
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

        var matchStatus = topCandidate.Score >= ambiguousThreshold
            ? TrackMatchingStatuses.Ambiguous
            : TrackMatchingStatuses.NoMatch;

        var statusReason = matchStatus == TrackMatchingStatuses.Ambiguous
            ? $"Multiple plausible candidates require review. Top cluster {topCandidate.Score:P0}; runner-up {secondDistinctScore:P0}; margin {margin:P0} is below the required {autoMatchMargin:P0}."
            : $"Candidates were found, but confidence stayed below the auto-match threshold. Top cluster {topCandidate.Score:P0}; runner-up {secondDistinctScore:P0}.";

        return new TrackMatchDecision
        {
            MatchStatus = matchStatus,
            ResolutionNotes = statusReason,
            AcceptedCandidate = null,
            TopScore = topCandidate.Score,
            SecondDistinctScore = secondDistinctScore,
            DecisionReason = $"Distinct clusters: {clusters.Count}. Top cluster {topCandidate.Score:P0}; runner-up {secondDistinctScore:P0}; margin {margin:P0}."
        };
    }
}