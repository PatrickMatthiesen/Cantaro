using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Cantaro.Api.Configuration;

public sealed class AioStreamsWatchStateOptions
{
    public const string SectionName = "AioStreamsWatchState";
    public const string Route = "/api/aiostreams-tracker";

    public int UserId { get; init; }
    public Guid MediaEpisodeId { get; init; }
    public string? MetaId { get; init; }
    public string? VideoId { get; init; }
    public int? Season { get; init; }
    public int Episode { get; init; }
    public string? TokenHash { get; init; }

    internal bool IsConfigured => UserId > 0 && MediaEpisodeId != Guid.Empty
        && !string.IsNullOrWhiteSpace(MetaId) && !string.IsNullOrWhiteSpace(VideoId)
        && Season is null or >= 0 && Episode > 0 && !string.IsNullOrEmpty(TokenHash);

    internal bool AcceptsToken(string? token)
        => token is { Length: 43 } && TokenHash is { Length: 43 }
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(TokenHash), Encoding.UTF8.GetBytes(HashToken(token)));

    internal static string HashToken(string token)
        => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
