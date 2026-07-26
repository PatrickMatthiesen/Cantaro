using System.Text.Json.Serialization;

namespace Cantaro.Api.Services.Spotify;

public sealed record SpotifyPlaylistSnapshot(
    string Id,
    string Name,
    string? Description,
    string? OwnerName,
    string? ImageUrl,
    string ExternalUrl,
    string? SnapshotId,
    int ItemCount);

public sealed record SpotifyTrackSnapshot(
    string Id,
    string Name,
    string Artist,
    string? AlbumName,
    string? ImageUrl,
    string ExternalUrl,
    string? AlbumUrl,
    string? Isrc,
    int DurationSeconds,
    DateTimeOffset? AddedAt,
    int Position);

public sealed record SpotifyPlaylistImportSnapshot(
    SpotifyPlaylistSnapshot Playlist,
    IReadOnlyList<SpotifyTrackSnapshot> Tracks);

public sealed class SpotifyTokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

public sealed class SpotifyProfileResponse
{
    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
}

internal sealed class SpotifyPage<T>
{
    [JsonPropertyName("items")]
    public List<T?> Items { get; init; } = [];

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

internal sealed class SpotifyPlaylistResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("owner")]
    public SpotifyOwnerResponse? Owner { get; init; }

    [JsonPropertyName("images")]
    public List<SpotifyImageResponse> Images { get; init; } = [];

    [JsonPropertyName("external_urls")]
    public SpotifyExternalUrlsResponse? ExternalUrls { get; init; }

    [JsonPropertyName("snapshot_id")]
    public string? SnapshotId { get; init; }

    [JsonPropertyName("items")]
    public SpotifyItemsReferenceResponse? ItemsReference { get; init; }
}

internal sealed class SpotifyOwnerResponse
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
}

internal sealed class SpotifyItemsReferenceResponse
{
    [JsonPropertyName("total")]
    public int Total { get; init; }
}

internal sealed class SpotifyPlaylistItemResponse
{
    [JsonPropertyName("added_at")]
    public DateTimeOffset? AddedAt { get; init; }

    [JsonPropertyName("is_local")]
    public bool IsLocal { get; init; }

    [JsonPropertyName("item")]
    public SpotifyItemResponse? Item { get; init; }
}

internal sealed class SpotifyItemResponse
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; init; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtistResponse> Artists { get; init; } = [];

    [JsonPropertyName("album")]
    public SpotifyAlbumResponse? Album { get; init; }

    [JsonPropertyName("external_urls")]
    public SpotifyExternalUrlsResponse? ExternalUrls { get; init; }

    [JsonPropertyName("external_ids")]
    public SpotifyExternalIdsResponse? ExternalIds { get; init; }
}

internal sealed class SpotifyArtistResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class SpotifyAlbumResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("images")]
    public List<SpotifyImageResponse> Images { get; init; } = [];

    [JsonPropertyName("external_urls")]
    public SpotifyExternalUrlsResponse? ExternalUrls { get; init; }
}

internal sealed class SpotifyImageResponse
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

internal sealed class SpotifyExternalUrlsResponse
{
    [JsonPropertyName("spotify")]
    public string? Spotify { get; init; }
}

internal sealed class SpotifyExternalIdsResponse
{
    [JsonPropertyName("isrc")]
    public string? Isrc { get; init; }
}

internal sealed class SpotifyErrorEnvelope
{
    [JsonPropertyName("error")]
    public SpotifyErrorResponse? Error { get; init; }
}

internal sealed class SpotifyErrorResponse
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

internal sealed class SpotifyTokenErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? Description { get; init; }
}
