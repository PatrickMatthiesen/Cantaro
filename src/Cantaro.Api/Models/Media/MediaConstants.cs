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
    public const string ProviderDisconnect = "provider_disconnect";
}

public static class MediaProviderOperationTypes
{
    public const string UpdateProgress = "update_progress";
    public const string UpdateStatus = "update_status";
}

public static class MediaProviderOperationStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Retrying = "retrying";
    public const string Failed = "failed";
}
