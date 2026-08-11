namespace Cantaro.Api.Services;

/// <summary>
/// Optional capability for providers that expose a related-media graph.
/// </summary>
public interface IMediaRelationGraphProvider
{
    string ProviderId { get; }

    Task<MediaProviderRelationGraphSnapshot> GetRelationGraphAsync(
        int userId,
        string providerMediaId,
        CancellationToken cancellationToken);
}
