namespace Cantaro.Api.Services;

internal static class TrackMatchClusterer
{
    public static IReadOnlyList<TrackMatchCluster> BuildClusters(IReadOnlyList<TrackMatchScoredCandidate> rankedCandidates, int clusterDurationToleranceSeconds)
    {
        if (rankedCandidates.Count == 0)
        {
            return [];
        }

        var clusters = new List<TrackMatchCluster>();

        var rankByCandidate = rankedCandidates
            .Select((candidate, index) => (candidate, index))
            .ToDictionary(item => item.candidate, item => item.index);
        var clusteringOrder = rankedCandidates
            .Where(candidate => candidate.Candidate.DurationSeconds.HasValue)
            .Concat(rankedCandidates.Where(candidate => !candidate.Candidate.DurationSeconds.HasValue));

        foreach (var candidate in clusteringOrder)
        {
            TrackMatchCluster? matchingCluster = null;
            foreach (var cluster in clusters)
            {
                if (!TryGetClusterReason(candidate, cluster.Representative, clusterDurationToleranceSeconds, out var clusterReason))
                {
                    continue;
                }

                matchingCluster = new TrackMatchCluster
                {
                    ClusterId = cluster.ClusterId,
                    ClusterReason = clusterReason,
                    Representative = cluster.Representative,
                    Members = [.. cluster.Members, candidate]
                };
                break;
            }

            if (matchingCluster == null)
            {
                clusters.Add(new TrackMatchCluster
                {
                    ClusterId = CreateClusterId(candidate),
                    ClusterReason = "representative",
                    Representative = candidate,
                    Members = [candidate]
                });
                continue;
            }

            var existingIndex = clusters.FindIndex(cluster => cluster.ClusterId == matchingCluster.ClusterId);
            clusters[existingIndex] = matchingCluster;
        }

        return clusters
            .OrderBy(cluster => cluster.Members.Min(member => rankByCandidate[member]))
            .ToArray();
    }

    private static bool TryGetClusterReason(
        TrackMatchScoredCandidate candidate,
        TrackMatchScoredCandidate representative,
        int clusterDurationToleranceSeconds,
        out string clusterReason)
    {
        if (HaveSharedStrongIdentifier(candidate.Candidate, representative.Candidate))
        {
            clusterReason = "shared-strong-identifier";
            return true;
        }

        var normalizedTitle = TrackTextNormalizer.Normalize(candidate.Candidate.Title);
        var normalizedArtist = TrackTextNormalizer.Normalize(candidate.Candidate.Artist);
        var representativeTitle = TrackTextNormalizer.Normalize(representative.Candidate.Title);
        var representativeArtist = TrackTextNormalizer.Normalize(representative.Candidate.Artist);

        var candidateCredits = GetCandidateCredits(candidate);
        var representativeCredits = GetCandidateCredits(representative);

        var bothHaveCredits = candidateCredits.Count > 0 && representativeCredits.Count > 0;
        var artistsMatch = bothHaveCredits
            ? candidateCredits.SequenceEqual(representativeCredits, StringComparer.Ordinal)
            : normalizedArtist == representativeArtist;

        var candidateDuration = candidate.Candidate.DurationSeconds;
        var representativeDuration = representative.Candidate.DurationSeconds;
        var oneDurationIsMissing = candidateDuration.HasValue != representativeDuration.HasValue;
        var durationsSupportSameCluster = oneDurationIsMissing
            || AreDurationsClose(candidateDuration, representativeDuration, clusterDurationToleranceSeconds);

        if (normalizedTitle == representativeTitle
            && artistsMatch
            && HaveEquivalentTitleSemantics(candidate.CandidateMetadata, representative.CandidateMetadata)
            && durationsSupportSameCluster)
        {
            clusterReason = oneDurationIsMissing
                ? "normalized-title-artist-missing-duration"
                : "normalized-title-artist-duration";
            return true;
        }

        clusterReason = string.Empty;
        return false;
    }

    internal static IReadOnlyList<string> GetCandidateCredits(TrackMatchScoredCandidate candidate)
    {
        return candidate.Candidate.ArtistCredits.Count > 0
            ? TrackMetadataParser.NormalizeArtistCredits(candidate.Candidate.ArtistCredits)
            : candidate.CandidateMetadata.ArtistCredits;
    }

    private static string CreateClusterId(TrackMatchScoredCandidate candidate)
    {
        var normalizedTitle = TrackTextNormalizer.Normalize(candidate.Candidate.Title);
        var normalizedArtist = TrackTextNormalizer.Normalize(candidate.Candidate.Artist);
        var versionMarkers = string.Join("+", candidate.CandidateMetadata.VersionMarkers);
        var playbackModifiers = string.Join("+", candidate.CandidateMetadata.PlaybackModifiers);
        return $"{normalizedTitle}|{normalizedArtist}|{versionMarkers}|{playbackModifiers}|{candidate.Candidate.DurationSeconds?.ToString() ?? "unknown"}";
    }

    internal static bool HaveSharedStrongIdentifier(TrackMatchSearchCandidate left, TrackMatchSearchCandidate right)
    {
        if (!string.IsNullOrWhiteSpace(left.MbidRecording) && string.Equals(left.MbidRecording, right.MbidRecording, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.Isrc)
            && string.Equals(left.Isrc, right.Isrc, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HaveEquivalentTitleSemantics(ParsedTrackMetadata left, ParsedTrackMetadata right)
    {
        return TrackMatchScorer.HaveSameMarkers(left.VersionMarkers, right.VersionMarkers)
            && TrackMatchScorer.HaveSameMarkers(left.PlaybackModifiers, right.PlaybackModifiers);
    }

    internal static bool AreDurationsClose(int? left, int? right, int clusterDurationToleranceSeconds)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return false;
        }

        return Math.Abs(left.Value - right.Value) <= clusterDurationToleranceSeconds;
    }
}
