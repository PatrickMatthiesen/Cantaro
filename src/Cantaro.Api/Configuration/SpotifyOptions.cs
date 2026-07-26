namespace Cantaro.Api.Configuration;

public sealed class SpotifyOptions
{
    public const string SectionName = "Spotify";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
