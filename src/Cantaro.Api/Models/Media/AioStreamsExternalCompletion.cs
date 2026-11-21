namespace Cantaro.Api.Models;

internal sealed class AioStreamsExternalCompletion
{
    public long Id { get; set; }
    public Guid MediaLibraryEntryId { get; set; }
    public int? FirstEpisode { get; set; }
    public int? LastEpisode { get; set; }
    public long At { get; set; }
    public long LastReceiptId { get; set; }
}
