using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services;

public static class ExtensionAuthSigningKeyResolver
{
    private const string DevelopmentSigningKey = "Cantaro.ExtensionAuth.Development.Signing.Key.2026.04.26";

    public static string ResolveSigningKey(ExtensionAuthOptions options, IHostEnvironment environment)
    {
        var configuredKey = options.JwtSigningKey?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            return ValidateSigningKey(configuredKey);
        }

        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return ValidateSigningKey(DevelopmentSigningKey);
        }

        throw new InvalidOperationException(
            $"{ExtensionAuthOptions.SectionName}:JwtSigningKey must be configured outside development and testing.");
    }

    private static string ValidateSigningKey(string signingKey)
    {
        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"{ExtensionAuthOptions.SectionName}:JwtSigningKey must be at least 32 characters long.");
        }

        return signingKey;
    }
}