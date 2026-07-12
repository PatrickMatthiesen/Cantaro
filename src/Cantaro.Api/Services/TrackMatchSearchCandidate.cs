namespace Cantaro.Api.Services;

public class TrackMatchSearchCandidate
{
    public required string CandidateSource { get; init; }
    public required string ExternalId { get; init; }
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public IReadOnlyList<string> ArtistCredits { get; init; } = [];
    public string? ArtistMusicBrainzId { get; init; }
    public string? ArtistSortName { get; init; }
    public string? MbidRecording { get; init; }
    public string? Isrc { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Explanation { get; init; }
    public string? RawMetadata { get; init; }
}
