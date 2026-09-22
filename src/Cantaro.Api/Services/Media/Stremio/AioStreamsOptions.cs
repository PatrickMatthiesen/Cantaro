using System.Diagnostics.CodeAnalysis;

namespace Cantaro.Api.Services;

public sealed class AioStreamsOptions
{
    public const string SectionName = "AioStreams";

    public string BaseUrl { get; set; } = string.Empty;

    public static bool HasValidBaseUrl(AioStreamsOptions options)
        => string.IsNullOrWhiteSpace(options.BaseUrl) || options.TryGetBaseUri(out _);

    internal bool TryGetBaseUri([NotNullWhen(true)] out Uri? baseUri)
    {
        baseUri = null;
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var configuredUri)
            || !string.Equals(configuredUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(configuredUri.Host)
            || !string.IsNullOrEmpty(configuredUri.UserInfo)
            || !string.IsNullOrEmpty(configuredUri.Query)
            || !string.IsNullOrEmpty(configuredUri.Fragment))
        {
            return false;
        }

        baseUri = new Uri(configuredUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return true;
    }
}
