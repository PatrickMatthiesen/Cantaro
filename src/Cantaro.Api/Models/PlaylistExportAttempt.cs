namespace Cantaro.Api.Models;

// An attempted export can make a later read circular evidence, even if the
// provider failed or its acknowledgement was lost. This identity set only
// suppresses ingestion. It contains no recording, entry or export permission.
public sealed class PlaylistExportAttempt
{
    public int UserId { get; set; }
    public required string Service { get; set; }
    public required string ExternalAccountId { get; set; }
    public required string ServicePlaylistId { get; set; }
    public required string ExternalTrackId { get; set; }
}
