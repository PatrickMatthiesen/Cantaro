using Microsoft.Extensions.Options;

namespace Cantaro.Api.Configuration;

public enum AuthenticationMode
{
    Local,
    GoogleOnly,
    Both
}

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public AuthenticationMode Mode { get; set; } = AuthenticationMode.Local;

    public GoogleAuthenticationOptions Google { get; set; } = new();

    public bool LocalLoginEnabled => Mode != AuthenticationMode.GoogleOnly;

    public bool GoogleEnabled => Mode != AuthenticationMode.Local;
}

public sealed class GoogleAuthenticationOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    // This is the callback path consumed by the Google authentication handler.
    // Cantaro completes the external login at /api/auth/google/callback.
    public string CallbackPath { get; set; } = "/signin-google";
}

public sealed class AuthenticationOptionsValidator : IValidateOptions<AuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationOptions options)
    {
        if (!Enum.IsDefined(options.Mode))
        {
            return ValidateOptionsResult.Fail("Authentication:Mode must be Local, Both, or GoogleOnly.");
        }
        if (!options.GoogleEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Google.ClientId))
        {
            failures.Add($"{AuthenticationOptions.SectionName}:Google:ClientId must be configured when Google authentication is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Google.ClientSecret))
        {
            failures.Add($"{AuthenticationOptions.SectionName}:Google:ClientSecret must be configured when Google authentication is enabled.");
        }

        if (!IsSafeCallbackPath(options.Google.CallbackPath)
            || string.Equals(options.Google.CallbackPath.TrimEnd('/'), "/api/auth/google/callback", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{AuthenticationOptions.SectionName}:Google:CallbackPath must be a local path beginning with '/'.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsSafeCallbackPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path[0] == '/'
            && !path.StartsWith("//", StringComparison.Ordinal)
            && !path.Contains('\\', StringComparison.Ordinal)
            && !path.Contains('?', StringComparison.Ordinal)
            && !path.Contains('#', StringComparison.Ordinal)
            && !path.Any(char.IsControl);
    }
}
