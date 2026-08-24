using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface IMediaProvider
{
    string ProviderId { get; }

    Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId, CancellationToken cancellationToken = default);

    string ResolveRedirectUri(CallbackUrlCandidates callbackUrls)
        => callbackUrls.Preferred;

    string BuildCodeChallenge(string codeVerifier)
        => MediaProviderPkce.BuildS256CodeChallenge(codeVerifier);

    string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge);

    Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
        int userId,
        string authorizationCode,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken);

    Task DisconnectAsync(int userId, CancellationToken cancellationToken);

    Task<MediaProviderLibraryImportResult> ImportLibraryAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(int userId, MediaCatalogSearchRequest request, CancellationToken cancellationToken);

    Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(int userId, string providerMediaId, CancellationToken cancellationToken);

    Task<MediaProviderMutationResult> UpdateProgressAsync(int userId, MediaProgressUpdateRequest request, CancellationToken cancellationToken);

    Task<MediaProviderMutationResult> UpdateStatusAsync(int userId, MediaStatusUpdateRequest request, CancellationToken cancellationToken);

    Task<MediaProviderMutationResult> UpdateScoreAsync(int userId, MediaScoreUpdateRequest request, CancellationToken cancellationToken);

    Task<MediaProviderMutationResult> SyncLibraryStateAsync(
        int userId,
        MediaLibraryStateSyncRequest request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException($"{ProviderId} does not support whole-library-state synchronization.");

    Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(int userId, string providerMediaId, CancellationToken cancellationToken);
}
