using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class FrontendUrlResolver : IFrontendUrlResolver
{
    private readonly IConfiguration _configuration;
    private readonly FrontendUrlOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FrontendUrlResolver(
        IConfiguration configuration,
        IOptions<FrontendUrlOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetFrontendUrl()
    {
        var requestOrigin = GetTrustedRequestOrigin(includeRequestBaseUrl: false);
        if (!string.IsNullOrWhiteSpace(requestOrigin))
        {
            return requestOrigin;
        }

        var aspireUrl = _configuration["services:web:http:0"]
            ?? _configuration["services:web:https:0"]
            ?? _configuration["services:web:0"];

        if (!string.IsNullOrWhiteSpace(aspireUrl))
        {
            return aspireUrl.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl.TrimEnd('/');
        }

        return GetCurrentRequestBaseUrl();
    }

    public string GetCurrentRequestBaseUrl()
    {
        var requestOrigin = GetTrustedRequestOrigin(includeRequestBaseUrl: true);
        if (!string.IsNullOrWhiteSpace(requestOrigin))
        {
            return requestOrigin;
        }

        var request = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("HttpContext is not available for URL resolution.");

        return $"{request.Scheme}://{request.Host}";
    }

    public string GetCallbackUrl(string relativePath)
    {
        return $"{GetCurrentRequestBaseUrl()}/{relativePath.TrimStart('/')}";
    }

    private string? GetTrustedRequestOrigin(bool includeRequestBaseUrl)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return null;
        }

        var origin = GetOriginHeaderUrl(request);
        if (IsTrustedOrigin(origin))
        {
            return origin!.TrimEnd('/');
        }

        var refererOrigin = GetRefererOrigin(request);
        if (IsTrustedOrigin(refererOrigin))
        {
            return refererOrigin!.TrimEnd('/');
        }

        var forwardedOrigin = GetForwardedOrigin(request);
        if (IsTrustedOrigin(forwardedOrigin))
        {
            return forwardedOrigin!.TrimEnd('/');
        }

        if (!includeRequestBaseUrl)
        {
            return null;
        }

        var requestBaseUrl = $"{request.Scheme}://{request.Host}";
        return IsTrustedOrigin(requestBaseUrl) ? requestBaseUrl.TrimEnd('/') : null;
    }

    private static string? GetOriginHeaderUrl(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        return string.IsNullOrWhiteSpace(origin) ? null : origin;
    }

    private static string? GetRefererOrigin(HttpRequest request)
    {
        var referer = request.GetTypedHeaders().Referer;
        return referer is null ? null : referer.GetLeftPart(UriPartial.Authority);
    }

    private static string? GetForwardedOrigin(HttpRequest request)
    {
        var forwardedHost = GetFirstHeaderValue(request, "X-Forwarded-Host");
        if (string.IsNullOrWhiteSpace(forwardedHost))
        {
            return null;
        }

        var forwardedProto = GetFirstHeaderValue(request, "X-Forwarded-Proto");
        var scheme = string.IsNullOrWhiteSpace(forwardedProto) ? request.Scheme : forwardedProto;
        return $"{scheme}://{forwardedHost}";
    }

    private static string? GetFirstHeaderValue(HttpRequest request, string headerName)
    {
        var value = request.Headers[headerName].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var commaIndex = value.IndexOf(',');
        return commaIndex < 0 ? value.Trim() : value[..commaIndex].Trim();
    }

    private bool IsTrustedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_options.TrustLoopbackOrigins && uri.IsLoopback)
        {
            return true;
        }

        return IsConfiguredTrustedOrigin(uri) || HasTrustedHostSuffix(uri.Host);
    }

    private bool IsConfiguredTrustedOrigin(Uri origin)
    {
        foreach (var trustedOrigin in _options.TrustedOrigins)
        {
            if (!Uri.TryCreate(trustedOrigin, UriKind.Absolute, out var trustedUri))
            {
                continue;
            }

            if (Uri.Compare(origin, trustedUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasTrustedHostSuffix(string host)
    {
        foreach (var suffix in _options.TrustedHostSuffixes)
        {
            var normalizedSuffix = suffix.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedSuffix)
                && host.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
