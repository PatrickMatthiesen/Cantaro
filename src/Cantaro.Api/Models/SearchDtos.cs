namespace Cantaro.Api.Models;

public static class SearchGroupStatuses
{
    public const string Ok = "ok";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
}

public sealed class SearchResponseDto
{
    public required string Query { get; init; }
    public required SearchGroupsDto Groups { get; init; }
}

public sealed class SearchGroupsDto
{
    public required SearchGroupDto Songs { get; init; }
    public required SearchGroupDto Artists { get; init; }
    public required SearchGroupDto Playlists { get; init; }
    public required SearchGroupDto Media { get; init; }
}

public sealed class SearchGroupDto
{
    public required string Status { get; init; }
    public required IReadOnlyList<SearchResultDto> Items { get; init; }
    public bool HasMore { get; init; }
    public string? Message { get; init; }
}

public sealed class SearchResultDto
{
    public required string EntityType { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Detail { get; init; }
    public string? ArtworkUrl { get; init; }
    public required string CanonicalRoute { get; init; }
}
