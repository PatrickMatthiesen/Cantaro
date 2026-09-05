using System.Text.RegularExpressions;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static partial class MediaDestinationUrlPolicy
{
    /// <summary>
    /// Removes query parameters and fragments from an observed URL before it is
    /// persisted. Invalid URLs are trimmed and returned unchanged so the
    /// existing validation path can report the provider-specific error.
    /// </summary>
    public static string NormalizeObservedUrl(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var queryIndex = trimmed.IndexOfAny(['?', '#']);
            return (queryIndex >= 0 ? trimmed[..queryIndex] : trimmed).Trim();
        }

        return uri.GetLeftPart(UriPartial.Path);
    }

    [GeneratedRegex(@"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?watch/([A-Z0-9]+)(?:/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CrunchyrollWatchPathRegex();

    [GeneratedRegex(@"^[A-Z0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex CrunchyrollSeriesIdRegex();

    [GeneratedRegex(@"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?series/([A-Z0-9]+)(?:/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CrunchyrollSeriesPathRegex();

    public static bool TryNormalizePath(
        string provider,
        string? observedUrl,
        string? expectedEpisodeId,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (!string.Equals(provider, MediaObservationSiteIdentifiers.Crunchyroll, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(observedUrl)
            || string.IsNullOrWhiteSpace(expectedEpisodeId)
            || !Uri.TryCreate(observedUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !IsCrunchyrollHost(uri.DnsSafeHost))
        {
            return false;
        }

        var match = CrunchyrollWatchPathRegex().Match(uri.AbsolutePath);
        if (!match.Success
            || !string.Equals(match.Groups[1].Value, expectedEpisodeId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedPath = $"/watch/{match.Groups[1].Value.ToUpperInvariant()}";
        return true;
    }

    public static string? BuildUrl(string provider, string providerUrlPath)
    {
        if (!string.Equals(provider, MediaObservationSiteIdentifiers.Crunchyroll, StringComparison.OrdinalIgnoreCase)
            || !TryNormalizePath(
                provider,
                $"https://www.crunchyroll.com{providerUrlPath}",
                ExtractCrunchyrollEpisodeId(providerUrlPath),
                out var normalizedPath))
        {
            return null;
        }

        return $"https://www.crunchyroll.com{normalizedPath}";
    }

    public static string? BuildSeriesUrl(string provider, string? providerSeriesId)
    {
        if (!string.Equals(provider, MediaObservationSiteIdentifiers.Crunchyroll, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(providerSeriesId)
            || !CrunchyrollSeriesIdRegex().IsMatch(providerSeriesId.Trim()))
        {
            return null;
        }

        return $"https://www.crunchyroll.com/series/{providerSeriesId.Trim().ToUpperInvariant()}";
    }

    public static bool TryNormalizeSeriesUrl(
        string provider,
        string? observedUrl,
        string? expectedSeriesId,
        out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (!string.Equals(provider, MediaObservationSiteIdentifiers.Crunchyroll, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(observedUrl)
            || string.IsNullOrWhiteSpace(expectedSeriesId)
            || !Uri.TryCreate(observedUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !IsCrunchyrollHost(uri.DnsSafeHost))
        {
            return false;
        }

        var match = CrunchyrollSeriesPathRegex().Match(uri.AbsolutePath);
        if (!match.Success
            || !string.Equals(match.Groups[1].Value, expectedSeriesId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUrl = $"https://www.crunchyroll.com/series/{match.Groups[1].Value.ToUpperInvariant()}";
        return true;
    }

    private static string? ExtractCrunchyrollEpisodeId(string path)
    {
        var match = CrunchyrollWatchPathRegex().Match(path);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsCrunchyrollHost(string host) =>
        string.Equals(host, "crunchyroll.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "www.crunchyroll.com", StringComparison.OrdinalIgnoreCase);
}
