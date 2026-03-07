using Cantaro.Api.Configuration;
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
        var request = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("HttpContext is not available for URL resolution.");

        return $"{request.Scheme}://{request.Host}";
    }

    public string GetCallbackUrl(string relativePath)
    {
        return $"{GetCurrentRequestBaseUrl()}/{relativePath.TrimStart('/')}";
    }
}