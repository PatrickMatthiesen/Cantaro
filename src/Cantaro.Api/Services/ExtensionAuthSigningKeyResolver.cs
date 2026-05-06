using Cantaro.Api.Configuration;

namespace Cantaro.Api.Services;

public static class ExtensionAuthSigningKeyResolver
{
    public static string ResolveSigningKey(ExtensionAuthOptions options)
    {
        var configuredKey = options.JwtSigningKey?.Trim();
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                $"{ExtensionAuthOptions.SectionName}:JwtSigningKey must be configured. Are you missing a configuration value or environment variable?");
        }

        return ValidateSigningKey(configuredKey);
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