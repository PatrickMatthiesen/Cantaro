using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Cantaro.Api.Models;

/// <summary>
/// Represents a media title observation submitted by the browser extension.
/// User-scoped: each user's observations are independent and never shared.
/// </summary>
public class MediaObservation
{
    [NotMapped]
    public string? RawPayload
    {
        get => SeriesTitle is null && EpisodeNumber is null ? null : JsonSerializer.Serialize(new { seriesTitle = SeriesTitle, episodeTitle = EpisodeTitle, episodeNumber = EpisodeNumber, seasonTitle = SeasonTitle, seasonNumber = SeasonNumber, providerSeriesId = ProviderSeriesId, nextEpisodeProviderId = NextEpisodeProviderId, nextEpisodeNumber = NextEpisodeNumber });
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            try
            {
                using var json = JsonDocument.Parse(value);
                var root = json.RootElement;
                string? String(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                int? Int(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : null;
                SeriesTitle ??= String("seriesTitle"); SeasonTitle ??= String("seasonTitle");
                ProviderSeriesId ??= String("providerSeriesId"); ProviderSeasonId ??= String("providerSeasonId");
                EpisodeTitle ??= String("episodeTitle"); EpisodeNumber ??= Int("episodeNumber"); SeasonNumber ??= Int("seasonNumber");
                ProviderSequenceNumber ??= Int("providerSequenceNumber"); ReleaseTrack ??= String("releaseTrack");
                NextEpisodeProviderId ??= String("nextEpisodeProviderId"); NextEpisodeUrl ??= String("nextEpisodeUrl");
                NextEpisodeTitle ??= String("nextEpisodeTitle"); NextEpisodeNumber ??= Int("nextEpisodeNumber"); NextEpisodeReleaseTrack ??= String("nextEpisodeReleaseTrack");
                var episodes = root.TryGetProperty("observedEpisodes", out var observed) ? observed : root.TryGetProperty("episodes", out var catalog) ? catalog : default;
                if (episodes.ValueKind == JsonValueKind.Array)
                    foreach (var item in episodes.EnumerateArray())
                    {
                        var id = item.TryGetProperty("providerEpisodeId", out var idp) ? idp.GetString() : null;
                        if (string.IsNullOrWhiteSpace(id) || Episodes.Any(e => e.ProviderEpisodeId == id)) continue;
                        Episodes.Add(new MediaObservationEpisode { Id = Guid.NewGuid(), ProviderEpisodeId = id, ProviderUrl = item.TryGetProperty("providerUrl", out var url) ? url.GetString() ?? string.Empty : string.Empty, EpisodeNumber = item.TryGetProperty("episodeNumber", out var ep) && ep.TryGetInt32(out var n) ? n : 0, EpisodeTitle = item.TryGetProperty("episodeTitle", out var title) ? title.GetString() : null, ReleaseTrack = item.TryGetProperty("releaseTrack", out var track) ? track.GetString() : null });
                    }
            }
            catch (JsonException) { }
        }
    }
    public Guid Id { get; set; }

    /// <summary>
    /// The user who owns this observation.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Identifier for the site where the observation was made (e.g., "anilist",
    /// "crunchyroll", "myanimelist", "unknown").
    /// </summary>
    public required string SiteIdentifier { get; set; }

    /// <summary>
    /// Full URL that was observed.
    /// </summary>
    public required string ObservedUrl { get; set; }

    /// <summary>
    /// Stable site-specific media identifier when extractable from the URL or
    /// page (e.g., AniList media ID, MAL ID). Null when not available.
    /// </summary>
    public string? SiteMediaId { get; set; }

    /// <summary>
    /// Title text extracted from the page at observation time.
    /// </summary>
    public required string ObservedTitle { get; set; }

    /// <summary>
    /// Opaque progress hint extracted from the page (e.g., "Episode 5",
    /// "Chapter 12"). Stored as-is; the backend does not parse this for MVP.
    /// </summary>
    public string? ProgressHint { get; set; }

    /// <summary>
    /// When the user was observed on the page (sent by the extension, not the
    /// server receipt time).
    /// </summary>
    public DateTimeOffset ObservedAt { get; set; }

    /// <summary>
    /// Version string of the extension that submitted the observation.
    /// </summary>
    public string? ExtensionVersion { get; set; }

    /// <summary>
    /// Series title extracted from the page, when the provider exposes one.
    /// </summary>
    public string? SeriesTitle { get; set; }

    public string? SeasonTitle { get; set; }

    public string? EpisodeTitle { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? ProviderSeriesId { get; set; }

    public string? ProviderSeasonId { get; set; }

    public int? SeasonNumber { get; set; }

    public int? ProviderSequenceNumber { get; set; }

    public string? ReleaseTrack { get; set; }

    public string? NextEpisodeProviderId { get; set; }

    public string? NextEpisodeUrl { get; set; }

    public string? NextEpisodeTitle { get; set; }

    public int? NextEpisodeNumber { get; set; }

    public string? NextEpisodeReleaseTrack { get; set; }

    /// <summary>
    /// True when this row came from the catalog-observation endpoint rather
    /// than a watch-page observation.
    /// </summary>
    public bool IsCatalogObservation { get; set; }

    /// <summary>
    /// Bounded provider episode evidence rendered on the observed page.
    /// </summary>
    public ICollection<MediaObservationEpisode> Episodes { get; set; } = [];

    /// <summary>
    /// Provider search choices shown to the user when automatic matching could
    /// not safely resolve the observation.
    /// </summary>
    public string? ProviderChoicesPayload { get; set; }

    /// <summary>
    /// JSON audit trail of user resolution decisions and overrides.
    /// </summary>
    public string? ResolutionHistoryPayload { get; set; }

    /// <summary>
    /// Matching/resolution lifecycle: pending, matched, ambiguous, no_match, rejected.
    /// </summary>
    public required string MatchStatus { get; set; }

    /// <summary>
    /// Canonical MediaTitle resolved for this observation, when matched.
    /// </summary>
    public Guid? MediaTitleId { get; set; }

    /// <summary>
    /// The candidate accepted by the user or automatic matcher.
    /// </summary>
    public Guid? AcceptedCandidateId { get; set; }

    /// <summary>
    /// Human-readable notes about the current resolution state.
    /// </summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>
    /// User-selected offset applied to the observed progress hint.
    /// </summary>
    public int? EpisodeOffset { get; set; }

    /// <summary>
    /// Effective progress value after applying <see cref="EpisodeOffset"/>.
    /// </summary>
    public int? ResolvedProgress { get; set; }

    /// <summary>
    /// Library entry created or selected while resolving this observation.
    /// </summary>
    public Guid? ResolvedLibraryEntryId { get; set; }

    public int MatchAttemptCount { get; set; }

    public DateTimeOffset? LastMatchAttemptedAt { get; set; }

    public string? LastMatchError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public ICollection<MediaObservationCandidate> Candidates { get; set; } = [];
}

/// <summary>
/// Structured provider episode evidence attached to an observation. URLs are
/// normalized before persistence and language lists contain only provider
/// availability metadata.
/// </summary>
public class MediaObservationEpisode
{
    public Guid Id { get; set; }

    public Guid MediaObservationId { get; set; }

    public required string ProviderEpisodeId { get; set; }

    public required string ProviderUrl { get; set; }

    public int EpisodeNumber { get; set; }

    public string? EpisodeTitle { get; set; }

    public string? ReleaseTrack { get; set; }

    public List<string> AvailableSubtitleLanguageCodes { get; set; } = [];

    public List<string> AvailableAudioLanguageCodes { get; set; } = [];

    public MediaObservation? MediaObservation { get; set; }
}
