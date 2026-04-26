namespace Cantaro.Api.Services;

public interface IMediaProviderRegistry
{
    bool IsSupported(string providerId);

    IMediaProvider GetRequired(string providerId);

    IReadOnlyCollection<string> GetSupportedProviderIds();
}
