namespace Cantaro.Api.Services;

public interface IPlatformRegistry
{
    bool IsSupported(string platformId);

    IPlatformService GetRequired(string platformId);

    IReadOnlyCollection<string> GetSupportedPlatformIds();
}