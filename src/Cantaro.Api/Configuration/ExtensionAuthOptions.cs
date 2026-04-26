using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class ExtensionAuthOptions
{
    public const string SectionName = "ExtensionAuth";

    [Range(1, 1440)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenLifetimeDays { get; set; } = 30;

    [Range(1, 30)]
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;

    public string JwtIssuer { get; set; } = "Cantaro.Api";

    public string JwtAudience { get; set; } = "cantaro-browser-extension";

    public string JwtSigningKey { get; set; } = string.Empty;

    public string FrontendLoginPath { get; set; } = "/extension-auth";
}