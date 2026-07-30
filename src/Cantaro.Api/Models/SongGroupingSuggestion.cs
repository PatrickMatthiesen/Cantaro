namespace Cantaro.Api.Models;

public static class SongGroupingSuggestionKinds
{
    public const string SameComposition = "same_composition";
}

public static class SongGroupingSuggestionStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

/// <summary>
/// A reviewable proposal to move a distinct recording from its placeholder
/// Song to an existing Song representing the same composition.
/// </summary>
public sealed class SongGroupingSuggestion
{
    public Guid Id { get; set; }
    public Guid CandidateTrackId { get; set; }
    public Guid SourceSongId { get; set; }
    public Guid TargetSongId { get; set; }
    public Guid AnchorTrackId { get; set; }
    public required string Kind { get; set; }
    public decimal Confidence { get; set; }
    public required string EvidenceJson { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }

    public Track? CandidateTrack { get; set; }
    public Song? SourceSong { get; set; }
    public Song? TargetSong { get; set; }
    public Track? AnchorTrack { get; set; }
    public User? ReviewedByUser { get; set; }
}
