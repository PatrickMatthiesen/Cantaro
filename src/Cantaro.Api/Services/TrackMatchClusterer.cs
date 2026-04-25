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

        foreach (var candidate in rankedCandidates)
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

        return clusters;
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

        if (normalizedTitle == representativeTitle
            && normalizedArtist == representativeArtist
            && HaveEquivalentTitleSemantics(candidate.CandidateMetadata, representative.CandidateMetadata)
            && AreDurationsClose(candidate.Candidate.DurationSeconds, representative.Candidate.DurationSeconds, clusterDurationToleranceSeconds))
        {
            clusterReason = "normalized-title-artist-duration";
            return true;
        }

        clusterReason = string.Empty;
        return false;
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