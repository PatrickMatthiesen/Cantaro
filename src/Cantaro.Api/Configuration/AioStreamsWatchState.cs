using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Cantaro.Api.Configuration;

internal static class AioStreamsWatchState
{
    public const string Route = "/api/aiostreams-tracker";
    public const string ManifestCorsPolicy = "AioStreamsManifest";

    internal static string HashToken(string token)
        => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
