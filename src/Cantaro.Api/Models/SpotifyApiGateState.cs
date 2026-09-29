namespace Cantaro.Api.Models;

/// <summary>Durable app-wide pacing and cooldown state for Spotify Web API calls.</summary>
public sealed class SpotifyApiGateState
{
    public int Id { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public bool NotBeforeIsQuotaExceeded { get; set; }
    public DateTimeOffset NextRequestAt { get; set; }
    public DateTimeOffset? NormalLastRateLimitAt { get; set; }
    public DateTimeOffset? QuotaLastRateLimitAt { get; set; }
    public int NormalMissingRetryAfterCount { get; set; }
    public int QuotaMissingRetryAfterCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
