namespace Cantaro.Api.Configuration;

public sealed class FrontendUrlOptions
{
    public const string SectionName = "Frontend";

    public string? BaseUrl { get; set; }

    public bool TrustLoopbackOrigins { get; set; }

    public string[] TrustedOrigins { get; set; } = [];

    public string[] TrustedHostSuffixes { get; set; } = [];
}
