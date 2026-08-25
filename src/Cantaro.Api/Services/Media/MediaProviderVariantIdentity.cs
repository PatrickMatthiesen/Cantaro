using System.Text.RegularExpressions;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public readonly record struct MediaProviderVariantIdentity(string? ContentKey, string? AudioLocale);

public static partial class MediaProviderVariantIdentities
{
    public static MediaProviderVariantIdentity Parse(string provider, string providerEpisodeId)
    {
        if (!string.Equals(provider, MediaObservationSiteIdentifiers.Crunchyroll, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(providerEpisodeId))
        {
            return default;
        }

        var match = CrunchyrollLocalizedId().Match(providerEpisodeId.Trim().ToUpperInvariant());
        if (!match.Success)
        {
            return default;
        }

        var suffix = match.Groups["locale"].Value;
        return new MediaProviderVariantIdentity(
            match.Groups["content"].Value,
            $"{suffix[..2].ToLowerInvariant()}-{suffix[2..].ToUpperInvariant()}");
    }

    [GeneratedRegex("^(?<content>[A-Z0-9]{10})(?<locale>[A-Z]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex CrunchyrollLocalizedId();
}
