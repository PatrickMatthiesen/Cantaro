using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Configuration;

public sealed class CantaroGoogleHandler(
    IOptionsMonitor<GoogleOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<FrontendUrlOptions> frontendOptions) : GoogleHandler(options, logger, encoder)
{
    private const string CallbackUriKey = "cantaro.google.callback_uri";

    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri)
    {
        var publicOrigin = frontendOptions.Value.HttpsBaseUrl;
        if (!string.IsNullOrWhiteSpace(publicOrigin))
        {
            if (!Uri.TryCreate(publicOrigin.Trim(), UriKind.Absolute, out var origin)
                || origin.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(origin.UserInfo))
            {
                throw new InvalidOperationException("Frontend:HttpsBaseUrl must be an absolute HTTPS origin.");
            }

            // Keep the handler's PathBase and CallbackPath, replacing only the
            // internal HTTP origin with the explicitly configured public origin.
            redirectUri = origin.GetLeftPart(UriPartial.Authority) + new Uri(redirectUri).AbsolutePath;
        }

        // OAuth protects these properties in state. Reuse the exact URI during
        // redemption, even if request headers or configuration change meanwhile.
        properties.Items[CallbackUriKey] = redirectUri;
        return base.BuildChallengeUrl(properties, redirectUri);
    }

    protected override Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        if (context.Properties.Items.Remove(CallbackUriKey, out var callbackUri)
            && !string.IsNullOrEmpty(callbackUri))
        {
            context = new OAuthCodeExchangeContext(context.Properties, context.Code, callbackUri);
        }

        return base.ExchangeCodeAsync(context);
    }
}
