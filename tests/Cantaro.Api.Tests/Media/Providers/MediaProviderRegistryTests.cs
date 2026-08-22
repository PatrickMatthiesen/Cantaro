using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class MediaProviderRegistryTests
{
    [Fact]
    public void IsSupported_AndGetRequired_AreCaseInsensitive()
    {
        var provider = new FakeMediaProvider("anilist");
        var registry = new MediaProviderRegistry([provider]);

        Assert.True(registry.IsSupported("AniList"));
        Assert.Same(provider, registry.GetRequired("ANILIST"));
    }

    [Fact]
    public void GetSupportedProviderIds_ReturnsRegisteredProviderIds()
    {
        var registry = new MediaProviderRegistry(
        [
            new FakeMediaProvider("anilist"),
            new FakeMediaProvider("mangadex")
        ]);

        Assert.Equal(["anilist", "mangadex"], registry.GetSupportedProviderIds().OrderBy(id => id).ToArray());
    }

    private sealed class FakeMediaProvider(string providerId) : IMediaProvider
    {
        public string ProviderId { get; } = providerId;

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge)
        {
            throw new NotSupportedException();
        }

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri, string codeVerifier, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
