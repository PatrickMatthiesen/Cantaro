namespace Cantaro.Api.Services;

public sealed class MediaProviderRegistry : IMediaProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IMediaProvider> _providersById;

    public MediaProviderRegistry(IEnumerable<IMediaProvider> providers)
    {
        _providersById = providers.ToDictionary(
            provider => provider.ProviderId,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSupported(string providerId)
    {
        return _providersById.ContainsKey(providerId);
    }

    public IMediaProvider GetRequired(string providerId)
    {
        if (!_providersById.TryGetValue(providerId, out var provider))
        {
            throw new KeyNotFoundException($"Media provider '{providerId}' is not supported");
        }

        return provider;
    }

    public IReadOnlyCollection<string> GetSupportedProviderIds()
    {
        return _providersById.Keys.ToArray();
    }
}
