namespace Cantaro.Api.Models;

/// <summary>
/// An authoritative mapping from one provider-owned series/season container to
/// a canonical Cantaro title. Provider season ordinals are metadata only and
/// never imply a position in a franchise graph.
/// </summary>
public sealed class MediaProviderSeasonMapping
{
    public Guid Id { get; set; }

    public required string Provider { get; set; }

    public required string ProviderSeriesId { get; set; }

    public string? ProviderSeasonId { get; set; }

    public int? ProviderSeasonNumber { get; set; }

    public Guid MediaTitleId { get; set; }

    /// <summary>
    /// Added to the provider episode number to obtain the canonical episode
    /// number within <see cref="MediaTitleId"/>.
    /// </summary>
    public int EpisodeOffset { get; set; }

    public required string MappingSource { get; set; }

    public decimal Confidence { get; set; }

    public bool HasConflict { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastVerifiedAt { get; set; }

    public MediaTitle? MediaTitle { get; set; }
}
