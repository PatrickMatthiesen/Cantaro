namespace Cantaro.Api.Models;

/// <summary>
/// Controlled musical-version traits. A Track can carry several traits at the
/// same time; these strings are intentionally not an exclusive enum.
/// </summary>
public static class TrackVersionTraitKeys
{
    public const string Original = "original";
    public const string Acoustic = "acoustic";
    public const string Orchestral = "orchestral";
    public const string Live = "live";
    public const string Instrumental = "instrumental";
    public const string ACappella = "a-cappella";
    public const string Remix = "remix";
    public const string Cover = "cover";
    public const string Edit = "edit";
    public const string Demo = "demo";
    public const string Remaster = "remaster";
    public const string Karaoke = "karaoke";
    public const string Stripped = "stripped";

    public static IReadOnlyList<string> All { get; } =
    [
        Original,
        Acoustic,
        Orchestral,
        Live,
        Instrumental,
        ACappella,
        Remix,
        Cover,
        Edit,
        Demo,
        Remaster,
        Karaoke,
        Stripped
    ];

    public static bool IsKnown(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// An auditable evidence assertion that a Track has one musical-version trait.
/// Revocation retains history instead of mutating the meaning of old evidence.
/// </summary>
public sealed class TrackVersionTrait
{
    public Guid Id { get; set; }
    public Guid TrackId { get; set; }
    public required string TraitKey { get; set; }
    public decimal Confidence { get; set; }
    public required string EvidenceSource { get; set; }
    public required string EvidenceIdentity { get; set; }
    public required string EvidenceMethod { get; set; }
    public string? MethodVersion { get; set; }
    public required string AssertedByType { get; set; }
    public required string AssertedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedByType { get; set; }
    public string? RevokedById { get; set; }
    public string? RevocationReason { get; set; }
    public Guid? SupersedesTraitId { get; set; }

    public Track? Track { get; set; }
    public TrackVersionTrait? SupersedesTrait { get; set; }
}
