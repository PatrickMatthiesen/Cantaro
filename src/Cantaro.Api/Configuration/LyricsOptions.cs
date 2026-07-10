using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class LyricsOptions
{
    public const string SectionName = "Lyrics";

    public bool Enabled { get; set; } = true;

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://lrclib.net";

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;

    [Range(1, 10_000)]
    public int CacheEntryLimit { get; set; } = 500;

    [Range(1, 24 * 60)]
    public int AvailableCacheMinutes { get; set; } = 60;

    [Range(1, 24 * 60)]
    public int UnavailableCacheMinutes { get; set; } = 10;
}
