namespace Cantaro.Api.Models;

public static class MediaKinds
{
    public const string Anime = "anime";
    public const string Manga = "manga";
    public const string Movie = "movie";
    public const string Series = "series";
    public const string Other = "other";
}

public static class MediaProgressDimensions
{
    public const string Episode = "episode";
    public const string Chapter = "chapter";
    public const string Volume = "volume";
    public const string CompletionOnly = "completion_only";
    public const string Unavailable = "unavailable";
}

public static class MediaLibraryStatuses
{
    public const string Current = "current";
    public const string Planned = "planned";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Dropped = "dropped";
    public const string Unknown = "unknown";
}

public static class MediaMappingSources
{
    public const string Imported = "imported";
    public const string Automatic = "automatic";
    public const string UserConfirmed = "user_confirmed";
    public const string NoMatch = "no_match";
}

public static class MediaMutationSources
{
    public const string ProviderImport = "provider_import";
    public const string UserProgressUpdate = "user_progress_update";
    public const string UserStatusUpdate = "user_status_update";
    public const string UserProviderIdentityCorrection = "user_provider_identity_correction";
    public const string ProviderDisconnect = "provider_disconnect";

    /// <summary>
    /// Progress was automatically advanced by a matched site observation,
    /// subject to monotonic and sync-metadata guards.
    /// </summary>
    public const string ObservationAutoProgress = "observation_auto_progress";
}

public static class MediaProviderOperationTypes
{
    public const string UpdateProgress = "update_progress";
    public const string UpdateStatus = "update_status";

    /// <summary>
    /// Progress update triggered automatically by a matched observation.
    /// Carries an <see cref="AutoProgressUpdatePayload"/> in <c>PayloadJson</c>
    /// so provenance is recorded alongside the update numbers.
    /// </summary>
    public const string AutoProgressUpdate = "auto_progress_update";
}

public static class MediaProviderOperationStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retrying = "retrying";
    public const string Failed = "failed";
}

public static class MediaObservationStatuses
{
    /// <summary>Observation received; candidate search not yet attempted.</summary>
    public const string Pending = "pending";

    /// <summary>Automatically matched to a single high-confidence MediaTitle.</summary>
    public const string Matched = "matched";

    /// <summary>Multiple plausible candidates; requires user review.</summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>Search ran but found no usable candidates.</summary>
    public const string NoMatch = "no_match";

    /// <summary>User explicitly rejected all candidates.</summary>
    public const string Rejected = "rejected";
}

public static class MediaObservationCandidateSources
{
    /// <summary>Direct lookup via provider link using the stable SiteMediaId.</summary>
    public const string ProviderLinkExact = "provider_link_exact";

    /// <summary>Fuzzy title match against the user's imported library.</summary>
    public const string LibraryTitleSearch = "library_title_search";

    /// <summary>Fuzzy title match across all canonical MediaTitles in the catalog.</summary>
    public const string CatalogTitleSearch = "catalog_title_search";
}

public static class MediaObservationSiteIdentifiers
{
    public const string AniList = "anilist";
    public const string MyAnimeList = "myanimelist";
    public const string Crunchyroll = "crunchyroll";
    public const string Unknown = "unknown";
}
