using System.Security.Cryptography;
using System.Text;

namespace Cantaro.Api.Services;

public static class MediaProviderPkce
{
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string BuildS256CodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
