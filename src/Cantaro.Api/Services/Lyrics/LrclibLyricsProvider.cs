using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services.Lyrics;

public sealed class LrclibLyricsProvider(
    IHttpClientFactory httpClientFactory,
    LyricsProviderCache cache,
    IOptions<LyricsOptions> options,
    ILogger<LrclibLyricsProvider> logger) : ILyricsProvider
{
    private const string ProviderName = "lrclib";
    private const string Attribution = "Lyrics provided by LRCLIB (https://lrclib.net)";
    private readonly LyricsOptions _options = options.Value;

    public async Task<LyricsResult> GetLyricsAsync(LyricsLookup lookup, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Status(LyricsStates.Disabled, LyricsMatchStatuses.Disabled, "The lyrics provider is disabled.");
        }

        var cacheKey = BuildCacheKey(lookup);
        if (cache.TryGet(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        LyricsResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            result = HasExactSignature(lookup)
                ? await TryExactThenSearchAsync(lookup, timeout.Token)
                : await SearchAsync(lookup, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("LRCLIB request timed out for track {TrackId}.", lookup.TrackId);
            result = Status(LyricsStates.ProviderError, LyricsMatchStatuses.ProviderError, "The lyrics provider timed out.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "LRCLIB request failed for track {TrackId}.", lookup.TrackId);
            result = Status(LyricsStates.ProviderError, LyricsMatchStatuses.ProviderError, "The lyrics provider is temporarily unavailable.");
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "LRCLIB returned an invalid response for track {TrackId}.", lookup.TrackId);
            result = Status(LyricsStates.ProviderError, LyricsMatchStatuses.ProviderError, "The lyrics provider returned an invalid response.");
        }

        var cacheMinutes = result.State is LyricsStates.Available or LyricsStates.Instrumental
            ? _options.AvailableCacheMinutes
            : _options.UnavailableCacheMinutes;
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(cacheMinutes));
        return result;
    }

    private async Task<LyricsResult> TryExactThenSearchAsync(LyricsLookup lookup, CancellationToken cancellationToken)
    {
        using var response = await GetAsync("api/get", new Dictionary<string, string?>
        {
            ["track_name"] = lookup.Title,
            ["artist_name"] = lookup.Artist,
            ["album_name"] = lookup.Album,
            ["duration"] = lookup.DurationSeconds?.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return await SearchAsync(lookup, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        var candidate = await response.Content.ReadFromJsonAsync<LrclibRecord>(cancellationToken);
        if (candidate is null || !IsExactMatch(lookup, candidate))
        {
            return await SearchAsync(lookup, cancellationToken);
        }

        return ToResult(candidate, LyricsMatchStatuses.Exact, 1m, "Matched LRCLIB's exact track signature.");
    }

    private async Task<LyricsResult> SearchAsync(LyricsLookup lookup, CancellationToken cancellationToken)
    {
        using var response = await GetAsync("api/search", new Dictionary<string, string?>
        {
            ["track_name"] = lookup.Title,
            ["artist_name"] = lookup.Artist,
            ["album_name"] = lookup.Album
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
        var candidates = await response.Content.ReadFromJsonAsync<List<LrclibRecord>>(cancellationToken) ?? [];
        var ranked = candidates
            .Select(candidate => new RankedCandidate(candidate, Score(lookup, candidate)))
            .Where(candidate => IsAcceptable(lookup, candidate))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Record.Id)
            .ToList();

        if (ranked.Count == 0)
        {
            return Status(LyricsStates.Unavailable, LyricsMatchStatuses.Unavailable, "LRCLIB has no confident match for this track.");
        }

        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < 0.08m)
        {
            return new LyricsResult
            {
                State = LyricsStates.Ambiguous,
                MatchStatus = LyricsMatchStatuses.Ambiguous,
                Provider = ProviderName,
                Confidence = ranked[0].Score,
                Attribution = Attribution,
                Explanation = "LRCLIB returned multiple similarly strong candidates; no lyrics were selected."
            };
        }

        return ToResult(ranked[0].Record, LyricsMatchStatuses.Fallback, ranked[0].Score, "Matched using a conservative LRCLIB search.");
    }

    private async Task<HttpResponseMessage> GetAsync(string path, IReadOnlyDictionary<string, string?> parameters, CancellationToken cancellationToken)
    {
        var query = string.Join("&", parameters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return await httpClientFactory.CreateClient("lrclib").GetAsync($"{path}?{query}", cancellationToken);
    }

    private static bool HasExactSignature(LyricsLookup lookup) =>
        !string.IsNullOrWhiteSpace(lookup.Title)
        && !string.IsNullOrWhiteSpace(lookup.Artist)
        && !string.IsNullOrWhiteSpace(lookup.Album)
        && lookup.DurationSeconds is > 0;

    private static bool IsExactMatch(LyricsLookup lookup, LrclibRecord candidate) =>
        Similarity(lookup.Title, candidate.TrackName) == 1m
        && Similarity(lookup.Artist, candidate.ArtistName) >= 0.8m
        && (!lookup.DurationSeconds.HasValue || Math.Abs(lookup.DurationSeconds.Value - candidate.Duration) <= 5);

    private static bool IsAcceptable(LyricsLookup lookup, RankedCandidate candidate) =>
        candidate.Score >= 0.90m
        && Similarity(lookup.Title, candidate.Record.TrackName) >= 0.90m
        && Similarity(lookup.Artist, candidate.Record.ArtistName) >= 0.80m
        && (!lookup.DurationSeconds.HasValue || Math.Abs(lookup.DurationSeconds.Value - candidate.Record.Duration) <= 10);

    private static decimal Score(LyricsLookup lookup, LrclibRecord candidate)
    {
        var score = Similarity(lookup.Title, candidate.TrackName) * 0.60m
            + Similarity(lookup.Artist, candidate.ArtistName) * 0.30m;
        score += string.IsNullOrWhiteSpace(lookup.Album)
            ? 0.05m
            : Similarity(lookup.Album, candidate.AlbumName) * 0.05m;

        if (!lookup.DurationSeconds.HasValue)
        {
            score += 0.05m;
        }
        else
        {
            var difference = Math.Abs(lookup.DurationSeconds.Value - candidate.Duration);
            score += difference switch
            {
                <= 2 => 0.05m,
                <= 5 => 0.035m,
                <= 10 => 0.015m,
                _ => 0m
            };
        }

        return Math.Round(score, 3, MidpointRounding.AwayFromZero);
    }

    private static decimal Similarity(string? left, string? right)
    {
        var leftWords = Normalize(left).Split([' '], StringSplitOptions.RemoveEmptyEntries);
        var rightWords = Normalize(right).Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (leftWords.Length == 0 || rightWords.Length == 0)
        {
            return 0m;
        }

        var leftSet = leftWords.ToHashSet(StringComparer.Ordinal);
        var rightSet = rightWords.ToHashSet(StringComparer.Ordinal);
        var union = leftSet.Union(rightSet).Count();
        return Math.Round((decimal)leftSet.Intersect(rightSet).Count() / union, 3, MidpointRounding.AwayFromZero);
    }

    private static string Normalize(string? value)
    {
        var characters = (value ?? string.Empty)
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split([' '], StringSplitOptions.RemoveEmptyEntries));
    }

    private static LyricsResult ToResult(LrclibRecord record, string matchStatus, decimal confidence, string explanation) => new()
    {
        State = record.Instrumental ? LyricsStates.Instrumental : LyricsStates.Available,
        MatchStatus = matchStatus,
        Provider = ProviderName,
        ProviderRecordId = record.Id.ToString(CultureInfo.InvariantCulture),
        PlainLyrics = record.Instrumental ? null : record.PlainLyrics,
        SyncedLyrics = record.Instrumental ? null : record.SyncedLyrics,
        Confidence = confidence,
        Attribution = Attribution,
        Explanation = explanation
    };

    private static LyricsResult Status(string state, string matchStatus, string explanation) => new()
    {
        State = state,
        MatchStatus = matchStatus,
        Provider = ProviderName,
        Attribution = Attribution,
        Explanation = explanation
    };

    private static string BuildCacheKey(LyricsLookup lookup) => string.Join('|',
        Normalize(lookup.Title),
        Normalize(lookup.Artist),
        Normalize(lookup.Album),
        lookup.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

    private sealed record RankedCandidate(LrclibRecord Record, decimal Score);

    private sealed class LrclibRecord
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("trackName")]
        public string? TrackName { get; init; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; init; }

        [JsonPropertyName("albumName")]
        public string? AlbumName { get; init; }

        [JsonPropertyName("duration")]
        public decimal Duration { get; init; }

        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; init; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; init; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; init; }
    }
}
