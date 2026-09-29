namespace Cantaro.Api.Services.Spotify;

/// <summary>
/// Local safety limits for Spotify requests. Spotify publishes no fixed safe
/// per-app request interval, so Cantaro paces requests conservatively.
/// </summary>
public sealed class SpotifyRequestGateOptions
{
    /// <summary>Minimum gap between Web API requests across all Cantaro instances.</summary>
    public TimeSpan RequestInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>First fallback delay when a normal 429 omits Retry-After.</summary>
    public TimeSpan MissingRetryAfterBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>First fallback delay when a quota 429 omits Retry-After.</summary>
    public TimeSpan QuotaMissingRetryAfterBaseDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum fallback delay. Explicit Spotify Retry-After values are never capped.</summary>
    public TimeSpan MissingRetryAfterMaxDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Reset a missing-header streak after this much quiet time.</summary>
    public TimeSpan BackoffResetQuietPeriod { get; set; } = TimeSpan.FromHours(24);
}

public sealed record SpotifyCooldownResult(
    DateTimeOffset NotBefore,
    TimeSpan RetryAfter,
    int FallbackCount,
    bool IsQuotaExceeded,
    bool Persisted = true);
