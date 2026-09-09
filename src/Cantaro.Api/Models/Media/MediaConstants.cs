namespace Cantaro.Api.Models;

public static class MediaKinds
{
    public const string Anime = "anime";
    public const string Manga = "manga";
    public const string Movie = "movie";
    public const string Series = "series";
    public const string Other = "other";
}

public static class MediaFormats
{
    public const string Tv = "tv";
    public const string TvShort = "tv_short";
    public const string Movie = "movie";
    public const string Special = "special";
    public const string Ova = "ova";
    public const string Ona = "ona";
    public const string Music = "music";
    public const string Manga = "manga";
    public const string Novel = "novel";
    public const string OneShot = "one_shot";
}

public static class MediaRelationTypes
{
    public const string Adaptation = "adaptation";
    public const string Prequel = "prequel";
    public const string Sequel = "sequel";
    public const string Parent = "parent";
    public const string SideStory = "side_story";
    public const string Character = "character";
    public const string Summary = "summary";
    public const string Alternative = "alternative";
    public const string SpinOff = "spin_off";
    public const string Other = "other";
    public const string Source = "source";
    public const string Compilation = "compilation";
    public const string Contains = "contains";
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
    public const string Repeating = "repeating";
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
    public const string UserScoreUpdate = "user_score_update";
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
    public const string UpdateScore = "update_score";

    public const string SyncLibraryState = "sync_library_state";

    /// <summary>
    /// Progress update triggered automatically by a matched observation.
    /// Carries an <see cref="AutoProgressUpdatePayload"/> in <c>PayloadJson</c>
    /// with only the provider target, progress values, and sync concurrency
    /// snapshot required to apply the update.
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
    /// <summary>Exact lookup through a previously catalogued provider episode identity.</summary>
    public const string ProviderEpisodeIdentityExact = "provider_episode_identity_exact";

    /// <summary>Exact lookup through an established provider series/season mapping.</summary>
    public const string ProviderSeasonMappingExact = "provider_season_mapping_exact";

    /// <summary>Direct lookup via provider link using the stable SiteMediaId.</summary>
    public const string ProviderLinkExact = "provider_link_exact";

    /// <summary>Fuzzy title match against the user's imported library.</summary>
    public const string LibraryTitleSearch = "library_title_search";

    /// <summary>Fuzzy title match across all canonical MediaTitles in the catalog.</summary>
    public const string CatalogTitleSearch = "catalog_title_search";
}

public static class MediaProviderSeasonMappingSources
{
    public const string ProviderEpisodeIdentity = "provider_episode_identity";
    public const string Manual = "manual";
}

public static class MediaObservationSiteIdentifiers
{
    public const string AniList = "anilist";
    public const string MyAnimeList = "myanimelist";
    public const string Crunchyroll = "crunchyroll";
    public const string Unknown = "unknown";

    public static bool IsStreamingService(string? siteIdentifier)
    {
        return siteIdentifier?.Trim().ToLowerInvariant() is
            Crunchyroll or
            "hidive" or
            "netflix" or
            "hulu" or
            "disney-plus" or
            "prime-video" or
            "max" or
            "apple-tv" or
            "paramount-plus" or
            "peacock" or
            "youtube";
    }
}
