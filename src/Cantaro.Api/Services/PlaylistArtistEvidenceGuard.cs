using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

internal static class PlaylistArtistEvidenceGuard
{
    // Call with canonical identity and mappings captured before this operation
    // materializes or maps its selected destination. Artist IDs, display credits,
    // similarity and user confirmation do not establish recording identity.
    public static bool CorroboratesRecording(
        Track track, TrackMatchSearchCandidate candidate, TrackSourceId? establishedMapping)
    {
        var canonicalIsrc = TrackIdentityResolver.NormalizeIsrc(track.Isrc);
        var candidateIsrc = TrackIdentityResolver.NormalizeIsrc(candidate.Isrc);
        var canonicalMbid = NormalizeRecordingId(track.MbidRecording);
        var candidateMbid = NormalizeRecordingId(candidate.MbidRecording);
        if (Conflicts(canonicalIsrc, candidateIsrc) || Conflicts(canonicalMbid, candidateMbid))
            return false;

        return establishedMapping is not null
                && establishedMapping.TrackId == track.Id
                && establishedMapping.SourceType == candidate.CandidateSource
                && establishedMapping.ExternalId == candidate.ExternalId
            || Matches(canonicalIsrc, candidateIsrc)
            || Matches(canonicalMbid, candidateMbid);
    }

    private static string? NormalizeRecordingId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && left == right;

    private static bool Conflicts(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && left != right;
}
