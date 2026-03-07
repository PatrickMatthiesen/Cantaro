namespace Cantaro.Api.Services;

public interface IFrontendUrlResolver
{
    string GetFrontendUrl();

    string GetCurrentRequestBaseUrl();

    string GetCallbackUrl(string relativePath);
}