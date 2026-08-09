namespace Cantaro.Api.Configuration;

public sealed class AnimeScheduleOptions
{
    public const string SectionName = "AnimeSchedule";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://animeschedule.net/api/v3";

    public string Id { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public int RefreshIntervalMinutes { get; set; } = 360;
}
