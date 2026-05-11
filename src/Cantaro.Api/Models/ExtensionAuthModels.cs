using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Models;

public sealed class ExtensionTokenRequest
{
    [FromForm(Name = "grant_type")]
    [Required]
    public required string GrantType { get; set; }

    [FromForm(Name = "code")]
    public string? Code { get; set; }

    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }

    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }

    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }

    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }
}

public sealed class ExtensionTokenRevokeRequest
{
    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }

    [FromForm(Name = "refresh_token")]
    [Required]
    public required string RefreshToken { get; set; }
}

public sealed class ExtensionTokenResponse
{
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("refresh_expires_in")]
    public required int RefreshExpiresIn { get; init; }

    [JsonPropertyName("user")]
    public required UserDto User { get; init; }
}