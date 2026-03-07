namespace Cantaro.Api.Configuration;

public sealed class FrontendUrlOptions
{
    public const string SectionName = "Frontend";

    public string? BaseUrl { get; set; }
}