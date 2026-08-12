namespace Cantaro.Api.Models;

/// <summary>
/// A directed provider assertion relating one canonical media title to another.
/// The direction and relation type are preserved as reported by the source
/// provider so each source title's outgoing assertions can be refreshed safely.
/// </summary>
public sealed class MediaTitleRelation
{
    public Guid Id { get; set; }

    public Guid MediaTitleId { get; set; }

    public Guid RelatedMediaTitleId { get; set; }

    public required string RelationType { get; set; }

    public required string SourceProvider { get; set; }

    public string? SourceRelationId { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastVerifiedAt { get; set; }

    public MediaTitle? MediaTitle { get; set; }

    public MediaTitle? RelatedMediaTitle { get; set; }
}
