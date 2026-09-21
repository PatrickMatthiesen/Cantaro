using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class SimklOptions
{
    public const string SectionName = "Simkl";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string AuthorizationUrl { get; set; } = "https://simkl.com/oauth2/authorize";

    public string TokenUrl { get; set; } = "https://api.simkl.com/oauth2/token";

    public string ApiBaseUrl { get; set; } = "https://api.simkl.com";
}
