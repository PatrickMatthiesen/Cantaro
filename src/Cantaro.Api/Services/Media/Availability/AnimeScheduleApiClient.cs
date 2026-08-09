using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class AnimeScheduleApiClient(
    HttpClient httpClient,
    IOptions<AnimeScheduleOptions> options)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly AnimeScheduleOptions _options = options.Value;

    public bool IsConfigured =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_options.Token);

    public Task<AnimeScheduleAnimePage?> GetAnimeByAniListIdsAsync(
        IReadOnlyCollection<string> aniListIds,
        CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("mt", "any")
        };
        query.AddRange(aniListIds
            .Where(id => int.TryParse(id, out _))
            .Select(id => new KeyValuePair<string, string?>("anilist-ids", id)));
        return GetAsync<AnimeScheduleAnimePage>("anime", query, cancellationToken);
    }

    public Task<IReadOnlyList<AnimeScheduleTimetableEntry>?> GetTimetableAsync(
        int? year,
        int? week,
        CancellationToken cancellationToken)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("tz", "Etc/UTC")
        };
        if (year is not null && week is not null)
        {
            query.Add(new("year", year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            query.Add(new("week", week.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return GetAsync<IReadOnlyList<AnimeScheduleTimetableEntry>>(
            "timetables/all",
            query,
            cancellationToken);
    }

    private async Task<T?> GetAsync<T>(
        string relativePath,
        IReadOnlyCollection<KeyValuePair<string, string?>> query,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return default;
        }

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var url = QueryHelpers.AddQueryString($"{baseUrl}/{relativePath}", query);
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
            request.Headers.UserAgent.ParseAdd("Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                await Task.Delay(ReadRateLimitDelay(response), cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
    }

    private static TimeSpan ReadRateLimitDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return ClampDelay(delta);
        }

        if (retryAfter?.Date is { } retryDate)
        {
            return ClampDelay(retryDate - DateTimeOffset.UtcNow);
        }

        if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var resetUnixSeconds))
        {
            return ClampDelay(DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds) - DateTimeOffset.UtcNow);
        }

        return TimeSpan.FromSeconds(1);
    }

    private static TimeSpan ClampDelay(TimeSpan delay) =>
        TimeSpan.FromMilliseconds(Math.Clamp(
            delay.TotalMilliseconds,
            TimeSpan.FromSeconds(1).TotalMilliseconds,
            TimeSpan.FromMinutes(2).TotalMilliseconds));
}

public sealed class AnimeScheduleAnimePage
{
    public List<AnimeScheduleAnime> Anime { get; set; } = [];
}

public sealed class AnimeScheduleAnime
{
    public required string Route { get; set; }

    public string? Status { get; set; }

    public int? Episodes { get; set; }

    public DateTimeOffset Premier { get; set; }

    public DateTimeOffset SubPremier { get; set; }

    public DateTimeOffset DubPremier { get; set; }

    public DateTimeOffset JpnTime { get; set; }

    public DateTimeOffset SubTime { get; set; }

    public DateTimeOffset DubTime { get; set; }

    public AnimeScheduleWebsites Websites { get; set; } = new();
}

public sealed class AnimeScheduleWebsites
{
    [JsonPropertyName("aniList")]
    public string? AniList { get; set; }
}

public sealed class AnimeScheduleTimetableEntry
{
    public required string Route { get; set; }

    public int EpisodeNumber { get; set; }

    public string AirType { get; set; } = string.Empty;

    public string AiringStatus { get; set; } = string.Empty;

    public DateTimeOffset EpisodeDate { get; set; }
}
