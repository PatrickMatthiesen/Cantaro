namespace Cantaro.Api.Models;

public class ConnectedAccountDto
{
    public required string PlatformId { get; set; }
    public bool IsConnected { get; set; }
    public string? DisplayName { get; set; }
    public string? ExternalAccountId { get; set; }
    public DateTime? ConnectedAt { get; set; }
}

public class PlatformPlaylistDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class PlatformSongDto
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ArtistName { get; set; }
    public int Index { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}