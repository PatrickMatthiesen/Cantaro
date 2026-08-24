using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class MyAnimeListOptions
{
    public const string SectionName = "MyAnimeList";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    [Required]
    public string AuthorizationUrl { get; set; } = "https://myanimelist.net/v1/oauth2/authorize";

    [Required]
    public string TokenUrl { get; set; } = "https://myanimelist.net/v1/oauth2/token";

    [Required]
    public string ApiBaseUrl { get; set; } = "https://api.myanimelist.net/v2";
}
