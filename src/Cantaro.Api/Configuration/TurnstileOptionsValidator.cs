using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Configuration;

public sealed class TurnstileOptionsValidator(IHostEnvironment environment) : IValidateOptions<TurnstileOptions>
{
    public ValidateOptionsResult Validate(string? name, TurnstileOptions options)
    {
        if (!environment.IsProduction()
            || !options.IsEnabled)
        {
            return ValidateOptionsResult.Success;
        }

        var loopbackHostnames = (options.AllowedHostnames ?? [])
            .Where(hostname => !string.IsNullOrWhiteSpace(hostname))
            .Where(IsLoopbackHostname)
            .ToArray();

        return loopbackHostnames.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Turnstile:AllowedHostnames cannot contain loopback hostnames in production: {string.Join(", ", loopbackHostnames)}.");
    }

    internal static bool IsLoopbackHostname(string hostname)
    {
        var normalized = hostname.Trim().TrimEnd('.');
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "::1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "[::1]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address);
    }
}
