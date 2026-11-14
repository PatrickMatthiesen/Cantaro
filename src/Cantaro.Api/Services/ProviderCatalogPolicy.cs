using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

/// <summary>Shared disclosure and identity use require independently acquired, current evidence.</summary>
internal static class ProviderCatalogPolicy
{
    // Keep the relational filter equivalent to IsVerified so selection and
    // pagination operate on current facts before materializing any display data.
    public static IQueryable<ProviderCatalogItem> CurrentSources(IQueryable<ProviderCatalogItem> sources, DateTimeOffset now) =>
        sources.Where(source => source.TrackId != null
            && source.AssociationState == RecordingAssociationState.Verified
            && source.AssociationFactsRevision == source.Revision
            && source.VisibilityState == CatalogVisibility.Public
            && source.VerifiedAt != null && source.VerifiedAt <= now
            && source.RevalidateAfter != null && source.RevalidateAfter > now
            && ((source.SourceType == "spotify" && source.VerificationMethod == CatalogVerificationMethods.SpotifyApplication)
                || (source.SourceType == "youtube" && source.VerificationMethod == CatalogVerificationMethods.YouTubePublicVideo)
                || (source.SourceType == "musicbrainz" && source.VerificationMethod == CatalogVerificationMethods.MusicBrainz)));

    public static bool IsUsable(ProviderCatalogItem? item, DateTimeOffset now) =>
        item is { VisibilityState: CatalogVisibility.Public, VerifiedAt: not null, RevalidateAfter: not null }
        && item.VerifiedAt <= now && item.RevalidateAfter > now
        && CatalogVerificationMethods.Matches(item.SourceType, item.VerificationMethod);

    public static bool IsVerified(ProviderCatalogItem? source, DateTimeOffset now) =>
        source is { TrackId: not null, AssociationState: RecordingAssociationState.Verified }
        && IsUsable(source, now) && source.AssociationFactsRevision == source.Revision;

    public static ProviderCatalogFacts? ReadFacts(ProviderCatalogItem? item, DateTimeOffset now)
    {
        if (!IsUsable(item, now)) return null;
        try
        {
            var facts = JsonSerializer.Deserialize<ProviderCatalogFacts>(item!.FactsJson);
            return string.IsNullOrWhiteSpace(facts?.Title) ? null : facts;
        }
        catch (JsonException) { return null; }
    }

    public static ProviderCatalogFacts? SharedFacts(Track? track, DateTimeOffset now)
    {
        var source = SharedSource(track, now);
        return ReadFacts(source, now);
    }

    public static ProviderCatalogItem? SharedSource(Track? track, DateTimeOffset now)
    {
        if (track is null) return null;
        return track.ProviderItems.Where(item => IsVerified(item, now))
            .OrderBy(item => item.SourceType, StringComparer.Ordinal).ThenBy(item => item.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static bool HasOwnedInput(TrackObservation? observation, int? userId, DateTimeOffset? now = null) =>
        ObservationInput.Read(observation, userId, now ?? DateTimeOffset.UtcNow) is not null;

    public static TrackMatchSearchCandidate Candidate(ProviderCatalogItem item, ProviderCatalogFacts facts) => new()
    {
        CandidateSource = item.SourceType, ExternalId = item.ExternalId, Title = facts.Title,
        Artist = facts.Artist, ArtistCredits = facts.ArtistCredits.Select(credit => credit.Name).ToArray(),
        ArtistIdentities = facts.ArtistCredits, Isrc = facts.Isrc,
        MbidRecording = facts.MusicBrainzRecordingId, DurationSeconds = facts.DurationSeconds,
        ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
    };
}
