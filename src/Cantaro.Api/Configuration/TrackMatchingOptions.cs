using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class TrackMatchingOptions
{
    public const string SectionName = "TrackMatching";

    [Range(1, 100)]
    public int MusicBrainzPerQueryResultLimit { get; set; } = 10;

    [Range(1, 100)]
    public int MusicBrainzMaxReturnedCandidates { get; set; } = 15;

    [Range(1, 20)]
    public int MusicBrainzMaxRequestsPerSearch { get; set; } = 5;

    [Range(0, 10)]
    public int MusicBrainzCollaboratorVariantLimit { get; set; } = 2;

    [Range(typeof(decimal), "0", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal AutoMatchThreshold { get; set; } = 0.85m;

    [Range(typeof(decimal), "0", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal AmbiguousThreshold { get; set; } = 0.65m;

    [Range(typeof(decimal), "0", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal AutoMatchMargin { get; set; } = 0.10m;

    [Range(typeof(decimal), "0", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal MinimumCandidateScore { get; set; } = 0.35m;

    [Range(0, 60)]
    public int ClusterDurationToleranceSeconds { get; set; } = 8;

    [Range(0, 60)]
    public int AutoMatchDurationToleranceSeconds { get; set; } = 10;

    [Range(0, 10)]
    public int DurationExactToleranceSeconds { get; set; } = 2;

    [Range(typeof(double), "0.1", "120", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public double DurationLongerHalfLifeSeconds { get; set; } = 13d;

    [Range(typeof(double), "0.1", "120", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public double DurationShorterHalfLifeSeconds { get; set; } = 5d;

    [Range(0, 600)]
    public int OfficialVideoPaddingMinSeconds { get; set; } = 30;

    [Range(0, 600)]
    public int OfficialVideoPaddingMaxSeconds { get; set; } = 300;

    [Range(typeof(decimal), "0", "10", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal OfficialVideoPaddingMaxRatio { get; set; } = 2.0m;

    [Range(typeof(decimal), "-1", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal PlaybackModifierMismatchPenalty { get; set; } = -0.20m;

    [Range(typeof(decimal), "-1", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal VersionMismatchPenalty { get; set; } = -0.15m;

    [Range(typeof(decimal), "-1", "1", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal VersionMarkerMatchBonus { get; set; } = 0.05m;
}
