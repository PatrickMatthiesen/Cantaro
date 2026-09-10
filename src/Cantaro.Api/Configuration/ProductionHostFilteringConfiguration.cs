namespace Cantaro.Api.Configuration;

public static class ProductionHostFilteringConfiguration
{
    public static void Configure(IConfiguration configuration, bool isProduction)
    {
        if (!isProduction)
        {
            return;
        }

        var configuredOrigin = configuration[$"{FrontendUrlOptions.SectionName}:HttpsBaseUrl"]?.Trim();
        if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var origin)
            || !string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || origin.HostNameType != UriHostNameType.Dns
            || string.Equals(origin.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || origin.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new InvalidOperationException(
                $"{FrontendUrlOptions.SectionName}:HttpsBaseUrl must be configured in Production as an HTTPS origin with a DNS hostname.");
        }

        configuration["AllowedHosts"] = origin.Host;
    }
}
