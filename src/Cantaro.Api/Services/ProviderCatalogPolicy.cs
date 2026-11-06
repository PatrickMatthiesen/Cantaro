using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

/// <summary>Shared disclosure and identity use require independently acquired, current evidence.</summary>
internal static class ProviderCatalogPolicy
{
    public static bool IsUsable(ProviderCatalogItem? item, DateTimeOffset now) =>
        item is { VisibilityState: CatalogVisibility.Public, VerifiedAt: not null, RevalidateAfter: not null }
        && item.VerifiedAt <= now && item.RevalidateAfter > now
        && CatalogVerificationMethods.Matches(item.SourceType, item.VerificationMethod);

    public static bool IsVerified(TrackSourceId? source, DateTimeOffset now) =>
        source is { AssociationState: RecordingAssociationState.Verified, ProviderCatalogItem: { } item }
        && IsUsable(item, now) && source.ProviderCatalogItemId == item.Id
        && source.SourceType == item.SourceType && source.ExternalId == item.ExternalId
        && source.CatalogRevision == item.Revision;

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
        return ReadFacts(source?.ProviderCatalogItem, now);
    }

    public static TrackSourceId? SharedSource(Track? track, DateTimeOffset now)
    {
        if (track is null) return null;
        return track.SourceIds.Where(item => IsVerified(item, now))
            .OrderBy(item => item.ProviderCatalogItemId == track.PreferredCatalogItemId ? 0 : 1)
            .ThenBy(item => item.SourceType, StringComparer.Ordinal).ThenBy(item => item.ExternalId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static bool HasOwnedSnapshot(TrackObservation? observation, int? userId) =>
        userId is > 0 && observation is not null && observation.OwnerUserId == userId
        && !string.IsNullOrWhiteSpace(observation.Title);

    public static TrackMatchSearchCandidate Candidate(ProviderCatalogItem item, ProviderCatalogFacts facts) => new()
    {
        CandidateSource = item.SourceType, ExternalId = item.ExternalId, Title = facts.Title,
        Artist = facts.Artist, ArtistCredits = facts.ArtistCredits.Select(credit => credit.Name).ToArray(),
        ArtistIdentities = facts.ArtistCredits, Isrc = facts.Isrc,
        MbidRecording = facts.MusicBrainzRecordingId, DurationSeconds = facts.DurationSeconds,
        ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision
    };
}
