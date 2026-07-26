namespace Cantaro.Api.Configuration;

public static class SpotifyRedirectUriValidator
{
    private const string LoopbackAddress = "127.0.0.1";

    public static bool TryValidate(string? value, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            error = "Spotify redirect URI must be an absolute URI.";
            return false;
        }

        if (ContainsWildcard(value))
        {
            error = "Spotify redirect URI must not contain wildcards.";
            return false;
        }

        if (ContainsUserInfo(value, uri))
        {
            error = "Spotify redirect URI must not contain user information.";
            return false;
        }

        if (value.Contains('#', StringComparison.Ordinal))
        {
            error = "Spotify redirect URI must not contain a fragment.";
            return false;
        }

        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            error = "Spotify redirect URI must include an absolute callback path.";
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(uri.Host, LoopbackAddress, StringComparison.Ordinal))
            {
                error = "HTTP Spotify redirect URIs are only allowed for 127.0.0.1.";
                return false;
            }

            return true;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Spotify redirect URI must use HTTPS, except for 127.0.0.1 development callbacks.";
            return false;
        }

        if (IsLocalhost(uri))
        {
            error = "HTTPS Spotify redirect URI must use a non-localhost host.";
            return false;
        }

        return true;
    }

    private static bool ContainsWildcard(string value)
    {
        if (value.Contains('*', StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return Uri.UnescapeDataString(value).Contains('*', StringComparison.Ordinal);
        }
        catch (UriFormatException)
        {
            return true;
        }
    }

    private static bool ContainsUserInfo(string value, Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0
            ? value[authorityStart..]
            : value[authorityStart..authorityEnd];
        return authority.Contains('@', StringComparison.Ordinal);
    }

    private static bool IsLocalhost(Uri uri)
    {
        var host = uri.Host.TrimEnd('.');
        return uri.IsLoopback
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
