using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

/// <summary>
/// Explicit matching input built from already loaded fresh public facts or the requesting owner's private input.
/// SourceType and ExternalId identify the provider item. Title is nonblank usable display input;
/// Artist, ThumbnailUrl and DurationSeconds are optional effective display values. Metadata contains
/// original title/channel, description, stable identifiers and structured credits for this operation.
/// It is not an EF entity or a persisted provider response. Reference observations have no input.
/// </summary>
public sealed record ObservationInput
{
    public required string SourceType { get; init; }
    public required string ExternalId { get; init; }
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int? DurationSeconds { get; init; }
    public IReadOnlyList<string> Albums { get; init; } = [];
    public TrackObservationMetadata Metadata { get; init; } = new();

    // SQL eligibility is shared by enqueue, recovery, claim and review queries.
    public static IQueryable<TrackObservation> Eligible(IQueryable<TrackObservation> observations, DateTimeOffset now) =>
        observations.Where(o => o.OwnerUserId > 0 &&
            ((o.InputKind == ObservationInputKind.Private && o.Title != null && o.Title.Trim() != "")
             || (o.InputKind == ObservationInputKind.Public && o.ProviderCatalogItem != null
                 && o.ProviderCatalogItemId == o.ProviderCatalogItem.Id
                 && o.SourceType == o.ProviderCatalogItem.SourceType && o.ExternalId == o.ProviderCatalogItem.ExternalId
                 && o.ProviderCatalogItem.VisibilityState == CatalogVisibility.Public
                 && o.ProviderCatalogItem.VerifiedAt != null && o.ProviderCatalogItem.VerifiedAt <= now
                 && o.ProviderCatalogItem.RevalidateAfter > now
                 && ((o.SourceType == "youtube" && o.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.YouTubePublicVideo)
                     || (o.SourceType == "spotify" && o.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.SpotifyApplication)
                     || (o.SourceType == "musicbrainz" && o.ProviderCatalogItem.VerificationMethod == CatalogVerificationMethods.MusicBrainz)))));

    internal static TrackObservationMetadata ReadOperationMetadata(string? json)
    {
        try { return JsonSerializer.Deserialize<TrackObservationMetadata>(json ?? "{}") ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static bool HasReviewedResult(TrackObservation observation) =>
        observation.ResolutionNotes == "Created canonical track from observation during manual review."
        || observation.AcceptedCandidateId != null
        && (observation.ResolutionNotes == "Resolved by manual candidate selection."
            || (observation.ResolutionNotes?.StartsWith("Created a ", StringComparison.Ordinal) == true
                && observation.ResolutionNotes.Contains("version Track", StringComparison.Ordinal)));

    public static void SetPublic(TrackObservation observation, ProviderCatalogItem item)
    {
        observation.InputKind = ObservationInputKind.Public;
        observation.ProviderCatalogItemId = item.Id;
        observation.ProviderCatalogItem = item;
        observation.Title = null; observation.Artist = null; observation.DurationSeconds = null;
        observation.ThumbnailUrl = null; observation.PrivateInputJson = null;
    }

    public static void SetPrivate(TrackObservation observation, string title, string? artist,
        int? duration, string? thumbnail, TrackObservationMetadata metadata)
    {
        observation.InputKind = ObservationInputKind.Private;
        observation.Title = title; observation.Artist = artist;
        observation.DurationSeconds = duration; observation.ThumbnailUrl = thumbnail;
        observation.PrivateInputJson = JsonSerializer.Serialize(new
        {
            OriginalTitle = metadata.OriginalTitle != title ? metadata.OriginalTitle : null,
            OriginalArtist = metadata.OriginalArtist != artist ? metadata.OriginalArtist : null,
            ChannelTitle = metadata.ChannelTitle != artist ? metadata.ChannelTitle : null, metadata.Description, metadata.Isrc, metadata.MbidRecording,
            ArtistIdentities = metadata.ArtistIdentities.Select(credit => credit with
            { ProfileUrl = null, ArtworkUrl = null, RawMetadata = null }).ToArray(), metadata.Album
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    }

    public static ObservationInput? Read(TrackObservation? observation, int? userId, DateTimeOffset now)
    {
        if (userId is not > 0 || observation is null || observation.OwnerUserId != userId) return null;
        if (observation.InputKind == ObservationInputKind.Public)
        {
            var item = observation.ProviderCatalogItem;
            return item is not null && item.Id == observation.ProviderCatalogItemId
                && item.SourceType == observation.SourceType && item.ExternalId == observation.ExternalId
                && ProviderCatalogPolicy.ReadFacts(item, now) is { } facts
                    ? FromFacts(item.SourceType, item.ExternalId, facts) : null;
        }
        if (observation.InputKind != ObservationInputKind.Private || string.IsNullOrWhiteSpace(observation.Title)) return null;
        TrackObservationMetadata metadata;
        try { metadata = JsonSerializer.Deserialize<TrackObservationMetadata>(observation.PrivateInputJson ?? "{}") ?? new(); }
        catch (JsonException) { return null; }
        return new ObservationInput
        {
            SourceType = observation.SourceType, ExternalId = observation.ExternalId,
            Title = observation.Title, Artist = observation.Artist,
            ThumbnailUrl = observation.ThumbnailUrl, DurationSeconds = observation.DurationSeconds,
            Metadata = new TrackObservationMetadata
            {
                OriginalTitle = metadata.OriginalTitle, OriginalArtist = metadata.OriginalArtist,
                ChannelTitle = metadata.ChannelTitle, Description = metadata.Description,
                Isrc = metadata.Isrc, MbidRecording = metadata.MbidRecording,
                ArtistIdentities = metadata.ArtistIdentities, Album = metadata.Album
            }
        };
    }

    public static ObservationInput FromFacts(string sourceType, string externalId, ProviderCatalogFacts facts) => new()
    {
        SourceType = sourceType, ExternalId = externalId, Title = facts.Title,
        Artist = facts.Artist, ThumbnailUrl = facts.ThumbnailUrl, DurationSeconds = facts.DurationSeconds, Albums = facts.Albums,
        Metadata = new TrackObservationMetadata
        {
            OriginalTitle = facts.OriginalTitle, ChannelTitle = facts.ChannelTitle,
            OriginalArtist = facts.ChannelTitle, Description = facts.Description,
            Isrc = facts.Isrc, MbidRecording = facts.MusicBrainzRecordingId,
            Album = facts.Albums.FirstOrDefault(), ArtistIdentities = facts.ArtistCredits
        }
    };
}
