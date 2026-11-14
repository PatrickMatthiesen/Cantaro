using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

// An accepted choice supplies owner metadata, never provider association or export evidence.
internal static class OwnedCandidateAnchorInput
{
    internal static IQueryable<TrackResolutionCandidate> Eligible(ApplicationDbContext db, int? owner, DateTimeOffset now)
        => db.TrackResolutionCandidates.AsNoTracking().Where(c => owner > 0
            && c.AnchorTrackId != null && c.IsAccepted && c.TrackObservation != null
            && c.TrackObservation.OwnerUserId == owner
            && c.TrackObservation.AcceptedCandidateId == c.Id
            && c.TrackObservation.MatchStatus == TrackMatchingStatuses.Matched
            && c.TrackObservation.TrackId != null && c.TrackObservation.TrackId != c.AnchorTrackId
            && ((c.ProviderCatalogItemId == null && c.Title != null && c.Title.Trim() != "")
                || (c.ProviderCatalogItem != null && c.ProviderCatalogItemId == c.ProviderCatalogItem.Id
                    && c.ProviderCatalogRevision == c.ProviderCatalogItem.Revision
                    && c.CandidateSource == c.ProviderCatalogItem.SourceType && c.ExternalId == c.ProviderCatalogItem.ExternalId
                    && c.ProviderCatalogItem.VisibilityState == CatalogVisibility.Public
                    && c.ProviderCatalogItem.VerifiedAt != null && c.ProviderCatalogItem.VerifiedAt <= now
                    && c.ProviderCatalogItem.RevalidateAfter > now
                    && ((c.CandidateSource == "youtube" && c.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.YouTubePublicVideo)
                        || (c.CandidateSource == "spotify" && c.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.SpotifyApplication)
                        || (c.CandidateSource == "musicbrainz" && c.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.MusicBrainz)))));

    internal static ObservationInput? Read(TrackResolutionCandidate candidate, DateTimeOffset now)
    {
        var value = TrackMatchCandidateInput.Read(candidate, now);
        if (value is null) return null;
        if (candidate.ProviderCatalogItem is { } item && ProviderCatalogPolicy.ReadFacts(item, now) is { } facts)
            return ObservationInput.FromFacts(item.SourceType, item.ExternalId, facts);
        return new ObservationInput
        {
            SourceType = value.CandidateSource, ExternalId = value.ExternalId, Title = value.Title,
            Artist = value.Artist, DurationSeconds = value.DurationSeconds,
            Metadata = new TrackObservationMetadata { Isrc = value.Isrc, MbidRecording = value.MbidRecording }
        };
    }

    internal static async Task<Dictionary<Guid, ObservationInput>> LoadAsync(ApplicationDbContext db, int owner,
        IEnumerable<Guid> trackIds, CancellationToken ct, bool requireArtist = false)
    {
        var ids = trackIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var now = DateTimeOffset.UtcNow;
        var collation = db.Database.IsNpgsql() ? "C" : "BINARY";
        var candidates = await Eligible(db, owner, now).Where(c => ids.Contains(c.AnchorTrackId!.Value))
            .Include(c => c.ProviderCatalogItem)
            .OrderBy(c => EF.Functions.Collate(c.CandidateSource, collation))
            .ThenBy(c => EF.Functions.Collate(c.ExternalId, collation)).ThenBy(c => c.Id).ToListAsync(ct);
        return candidates.Select(c => (Id: c.AnchorTrackId!.Value, Input: Read(c, now)))
            .Where(c => c.Input is not null && (!requireArtist || !string.IsNullOrWhiteSpace(c.Input.Artist)))
            .GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Input!);
    }

    internal static async Task<List<Guid>> FindAnchorIdsAsync(ApplicationDbContext db, int? owner,
        string source, string externalId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await Eligible(db, owner, now).Where(c => c.CandidateSource == source && c.ExternalId == externalId)
            .Include(c => c.ProviderCatalogItem).ToListAsync(ct);
        return candidates.Where(c => Read(c, now) is not null).Select(c => c.AnchorTrackId!.Value).Distinct().ToList();
    }
}
