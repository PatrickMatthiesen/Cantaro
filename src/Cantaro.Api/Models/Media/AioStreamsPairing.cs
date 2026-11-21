namespace Cantaro.Api.Models;

internal sealed class AioStreamsPairing
{
    public int UserId { get; set; }
    public required string TokenHash { get; set; }
    public string? PlaybackInstanceUrl { get; set; }
}
