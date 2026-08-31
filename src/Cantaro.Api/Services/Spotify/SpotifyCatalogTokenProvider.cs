using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services.Spotify;

public sealed class SpotifyCatalogTokenProvider(
    SpotifyApiClient apiClient,
    IOptions<SpotifyOptions> options,
    TimeProvider timeProvider,
    SpotifyCatalogTokenCache cache)
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(1);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ClientId)
        && !string.IsNullOrWhiteSpace(options.Value.ClientSecret);

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        if (cache.AccessToken != null && cache.ExpiresAt > timeProvider.GetUtcNow().Add(ExpirySkew))
        {
            return cache.AccessToken;
        }

        await cache.RefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (cache.AccessToken != null && cache.ExpiresAt > timeProvider.GetUtcNow().Add(ExpirySkew))
            {
                return cache.AccessToken;
            }

            var token = await apiClient.GetClientCredentialsTokenAsync(cancellationToken);
            cache.AccessToken = token.AccessToken;
            cache.ExpiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(0, token.ExpiresIn));
            return cache.AccessToken;
        }
        finally
        {
            cache.RefreshGate.Release();
        }
    }
}

public sealed class SpotifyCatalogTokenCache
{
    internal SemaphoreSlim RefreshGate { get; } = new(1, 1);
    internal string? AccessToken { get; set; }
    internal DateTimeOffset ExpiresAt { get; set; }
}
