namespace Cantaro.Api.Models;

public sealed class MediaProviderEpisode
{
    public required string Provider { get; set; }
    public required string ProviderEpisodeId { get; set; }
    public string? ProviderSeriesId { get; set; }
    public string? ProviderSeasonId { get; set; }
    public int? ProviderSeasonNumber { get; set; }
    public string? NormalizedSeasonTitle { get; set; }
    public int? ProviderEpisodeNumber { get; set; }
    public int? ProviderSequenceNumber { get; set; }
    public required string ProviderUrlPath { get; set; }
    public string? ContentKey { get; set; }
    public string? AudioLocale { get; set; }
    public string? ReleaseTrack { get; set; }
    public string? EpisodeTitle { get; set; }
    public string[] AvailableSubtitleLanguageCodes { get; set; } = [];
    public string[] AvailableAudioLanguageCodes { get; set; } = [];
    public int SeenCount { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
