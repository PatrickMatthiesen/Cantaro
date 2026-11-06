using Cantaro.Api.Models;
using System.Text.Json;

namespace Cantaro.Api.Services;

/// <summary>Allowlisted provider facts. Account responses and search queries are not catalog evidence.</summary>
public sealed class ProviderCatalogFacts
{
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public IReadOnlyList<string> Albums { get; init; } = [];
    public string? ThumbnailUrl { get; init; }
    public string? ProviderUrl { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Isrc { get; init; }
    public string? MusicBrainzRecordingId { get; init; }
    public IReadOnlyList<ArtistCreditMetadata> ArtistCredits { get; init; } = [];
    public TrackVersionFlags VersionFlags { get; init; }
}

internal static class CatalogVerificationMethods
{
    public const string SpotifyApplication = "spotify_application_catalog";
    public const string YouTubePublicVideo = "youtube_public_video_response";
    public const string MusicBrainz = "musicbrainz_recording_response";

    public static bool Matches(string source, string? method) => (source, method) is
        ("spotify", SpotifyApplication) or ("youtube", YouTubePublicVideo) or ("musicbrainz", MusicBrainz);
}

internal readonly record struct ProviderCatalogNormalizedFacts(
    string? Title,
    string? Artist,
    string? Isrc,
    string? MusicBrainzRecordingId)
{
    public static ProviderCatalogNormalizedFacts FromFacts(ProviderCatalogFacts facts) => new(
        NormalizeText(facts.Title),
        NormalizeText(facts.Artist),
        TrackIdentityResolver.NormalizeIsrc(facts.Isrc),
        NormalizeText(facts.MusicBrainzRecordingId));

    public static ProviderCatalogNormalizedFacts FromJson(string? factsJson)
    {
        if (string.IsNullOrWhiteSpace(factsJson)) return default;
        try
        {
            var facts = JsonSerializer.Deserialize<ProviderCatalogFacts>(factsJson);
            return facts is null ? default : FromFacts(facts);
        }
        catch (JsonException) { return default; }
    }

    private static string? NormalizeText(string? value)
    {
        var normalized = TrackTextNormalizer.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
