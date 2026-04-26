using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public class AniListOptions
{
    public const string SectionName = "AniList";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    [Required]
    public string AuthorizationUrl { get; set; } = "https://anilist.co/api/v2/oauth/authorize";

    [Required]
    public string TokenUrl { get; set; } = "https://anilist.co/api/v2/oauth/token";

    [Required]
    public string GraphQlUrl { get; set; } = "https://graphql.anilist.co";
}
