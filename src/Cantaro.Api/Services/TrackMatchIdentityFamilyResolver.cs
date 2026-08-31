namespace Cantaro.Api.Services;

internal static class TrackMatchIdentityFamilyResolver
{
    public static IReadOnlyList<TrackMatchIdentityFamily> BuildFamilies(
        IReadOnlyList<TrackMatchScoredCandidate> rankedCandidates)
    {
        var eligibleCandidates = rankedCandidates
            .Where(candidate => candidate.IsAutoMatchEligible)
            .ToArray();
        if (eligibleCandidates.Length == 0)
        {
            return [];
        }

        var providerConsensusByIsrc = eligibleCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Candidate.Isrc))
            .GroupBy(candidate => candidate.Candidate.Isrc!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(candidate => candidate.Candidate.CandidateSource)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                StringComparer.OrdinalIgnoreCase);

        return eligibleCandidates
            .GroupBy(CreateFamilyId, StringComparer.Ordinal)
            .Select(group =>
            {
                var members = group.ToArray();
                var representative = members
                    .OrderByDescending(candidate => GetProviderConsensus(candidate, providerConsensusByIsrc))
                    .ThenBy(candidate => candidate.DurationDifferenceSeconds ?? int.MaxValue)
                    .ThenByDescending(candidate => candidate.Score)
                    .ThenByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.Candidate.MbidRecording))
                    .ThenBy(candidate => candidate.Candidate.ExternalId, StringComparer.Ordinal)
                    .First();

                return new TrackMatchIdentityFamily
                {
                    FamilyId = group.Key,
                    Representative = representative,
                    Members = members,
                    ProviderConsensusCount = members.Max(candidate =>
                        GetProviderConsensus(candidate, providerConsensusByIsrc))
                };
            })
            .OrderByDescending(family => family.ProviderConsensusCount)
            .ThenByDescending(family => family.Representative.Score)
            .ThenBy(family => family.Representative.DurationDifferenceSeconds ?? int.MaxValue)
            .ThenBy(family => family.FamilyId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateFamilyId(TrackMatchScoredCandidate candidate)
    {
        var title = TrackTextNormalizer.Normalize(candidate.CandidateMetadata.SearchTitle);
        var credits = string.Join("+", TrackMatchClusterer.GetCandidateCredits(candidate));
        var versions = string.Join("+", candidate.CandidateMetadata.VersionMarkers);
        var playback = string.Join("+", candidate.CandidateMetadata.PlaybackModifiers);
        var recordingIdentity = string.IsNullOrWhiteSpace(candidate.Candidate.Isrc)
            ? "unidentified"
            : $"isrc:{candidate.Candidate.Isrc.Trim().ToUpperInvariant()}";
        return $"{title}|{credits}|{versions}|{playback}|{recordingIdentity}";
    }

    private static int GetProviderConsensus(
        TrackMatchScoredCandidate candidate,
        IReadOnlyDictionary<string, int> providerConsensusByIsrc)
    {
        return !string.IsNullOrWhiteSpace(candidate.Candidate.Isrc)
            && providerConsensusByIsrc.TryGetValue(candidate.Candidate.Isrc, out var providerCount)
                ? providerCount
                : 1;
    }
}
