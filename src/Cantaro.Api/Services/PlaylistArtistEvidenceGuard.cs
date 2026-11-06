using System.Collections.Frozen;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

internal static class PlaylistArtistEvidenceGuard
{
    // Capture before external search or destination materialization. A locally
    // resolved Track can be tracked, with only a partially fixed-up SourceIds
    // collection. Read every established key and retain no EF entities.
    public static async Task<RecordingEvidence> CaptureAsync(
        ApplicationDbContext dbContext, Track track, CancellationToken cancellationToken)
    {
        var isrc = TrackIdentityResolver.NormalizeIsrc(track.Isrc);
        var mbid = NormalizeRecordingId(track.MbidRecording);
        var mappings = await dbContext.TrackSourceIds.AsNoTracking()
            .Where(source => source.TrackId == track.Id)
            .Select(source => new { source.SourceType, source.ExternalId })
            .ToArrayAsync(cancellationToken);
        return new RecordingEvidence(isrc, mbid,
            mappings.Select(source => (source.SourceType, source.ExternalId)).ToFrozenSet());
    }

    internal sealed class RecordingEvidence(
        string? isrc, string? mbid, FrozenSet<(string SourceType, string ExternalId)> mappingKeys)
    {
        // Artist IDs, display credits, similarity and user confirmation do not
        // establish recording identity. Conflicting stable IDs reject enrichment
        // even when a provider recording mapping was already established.
        public bool CorroboratesRecording(TrackMatchSearchCandidate candidate)
        {
            var candidateIsrc = TrackIdentityResolver.NormalizeIsrc(candidate.Isrc);
            var candidateMbid = NormalizeRecordingId(candidate.MbidRecording);
            if (Conflicts(isrc, candidateIsrc) || Conflicts(mbid, candidateMbid))
                return false;

            return mappingKeys.Contains((candidate.CandidateSource, candidate.ExternalId))
                || Matches(isrc, candidateIsrc)
                || Matches(mbid, candidateMbid);
        }
    }

    private static string? NormalizeRecordingId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool Matches(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && left == right;

    private static bool Conflicts(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && left != right;
}
