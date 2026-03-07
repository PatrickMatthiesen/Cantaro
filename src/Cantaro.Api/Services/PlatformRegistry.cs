namespace Cantaro.Api.Services;

public sealed class PlatformRegistry : IPlatformRegistry
{
    private readonly IReadOnlyDictionary<string, IPlatformService> _servicesByPlatformId;

    public PlatformRegistry(IEnumerable<IPlatformService> platformServices)
    {
        _servicesByPlatformId = platformServices.ToDictionary(
            service => service.PlatformId,
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSupported(string platformId)
    {
        return _servicesByPlatformId.ContainsKey(platformId);
    }

    public IPlatformService GetRequired(string platformId)
    {
        if (!_servicesByPlatformId.TryGetValue(platformId, out var service))
        {
            throw new KeyNotFoundException($"Platform '{platformId}' is not supported");
        }

        return service;
    }

    public IReadOnlyCollection<string> GetSupportedPlatformIds()
    {
        return _servicesByPlatformId.Keys.ToArray();
    }
}