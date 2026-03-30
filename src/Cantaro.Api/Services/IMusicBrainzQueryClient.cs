namespace Cantaro.Api.Services;

public interface IMusicBrainzQueryClient
{
    Task<IReadOnlyList<MusicBrainzRecordingMatch>> FindRecordingsAsync(string query, int limit, CancellationToken cancellationToken);
}

public sealed class MusicBrainzRecordingMatch
{
    public required string ExternalId { get; init; }
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public string? MbidRecording { get; init; }
    public string? Isrc { get; init; }
    public int? DurationSeconds { get; init; }
    public int SearchScore { get; init; }
    public string? RawMetadata { get; init; }
}