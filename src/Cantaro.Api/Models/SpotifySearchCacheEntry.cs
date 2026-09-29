namespace Cantaro.Api.Models;

/// <summary>Shared public catalogue results. Never contains user tokens or playlist data.</summary>
public sealed class SpotifySearchCacheEntry
{
    public required string Key { get; set; }
    public required string ResultsJson { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
