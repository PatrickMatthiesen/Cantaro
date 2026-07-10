namespace Cantaro.Api.Services.Lyrics;

public static class LyricsStates
{
    public const string Available = "available";
    public const string Instrumental = "instrumental";
    public const string Unavailable = "unavailable";
    public const string Ambiguous = "ambiguous";
    public const string Disabled = "disabled";
    public const string ProviderError = "provider_error";
}

public static class LyricsMatchStatuses
{
    public const string Exact = "exact";
    public const string Fallback = "fallback";
    public const string Ambiguous = "ambiguous";
    public const string Unavailable = "unavailable";
    public const string Disabled = "disabled";
    public const string ProviderError = "provider_error";
}

public sealed record LyricsLookup(
    Guid TrackId,
    string Title,
    string Artist,
    string? Album,
    int? DurationSeconds,
    string? MbidRecording,
    string? Isrc);

public sealed record LyricsResult
{
    public required string State { get; init; }
    public required string MatchStatus { get; init; }
    public required string Provider { get; init; }
    public string? ProviderRecordId { get; init; }
    public string? PlainLyrics { get; init; }
    public string? SyncedLyrics { get; init; }
    public decimal? Confidence { get; init; }
    public required string Attribution { get; init; }
    public string? Explanation { get; init; }
}
