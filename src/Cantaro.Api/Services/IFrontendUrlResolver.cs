namespace Cantaro.Api.Services;

public sealed record CallbackUrlCandidates(
    string Preferred,
    string? Http,
    string? Https);

public interface IFrontendUrlResolver
{
    string GetFrontendUrl();

    string GetCurrentRequestBaseUrl();

    string GetCallbackUrl(string relativePath);

    CallbackUrlCandidates GetCallbackUrls(string relativePath)
    {
        var preferred = GetCallbackUrl(relativePath);
        var scheme = new Uri(preferred).Scheme;
        return new CallbackUrlCandidates(
            preferred,
            string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? preferred
                : null,
            string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? preferred
                : null);
    }
}
