using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public sealed class AioStreamsAnimeEnricher(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<AioStreamsOptions> options,
    TimeProvider timeProvider) : IAioStreamsAnimeEnricher
{
    internal const string HttpClientName = "aiostreams-anime";
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan FreshDuration = TimeSpan.FromHours(24);
    internal static readonly TimeSpan StaleDuration = TimeSpan.FromDays(7);
    internal static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AioStreamsOptions _options = options.Value;

    public async Task EnrichAsync(MediaProviderTitleDetails details, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(details.MediaKind, MediaKinds.Anime, StringComparison.Ordinal)
            || !TryGetSourceIdentity(details.ProviderId, details.ProviderMediaId, out var source))
        {
            return;
        }

        var originalTargets = GetOriginalTargets(details);
        var locallyEnrichedTargets = EnrichKnownKitsuTarget(details, originalTargets);
        if (!ReferenceEquals(locallyEnrichedTargets, originalTargets))
        {
            details.StremioTargets = locallyEnrichedTargets;
        }

        if (!_options.TryGetBaseUri(out var baseUri)) return;

        var (entry, _, _) = await GetEntryAsync(baseUri, source, cancellationToken);
        if (entry is null || !HasExactSourceIdentity(entry, source)) return;

        var candidates = BuildCandidates(details, entry);
        if (candidates is null)
        {
            details.AioStreamsIdentityConflict = true;
            return;
        }

        if (candidates.Count == 0) return;

        var currentTargets = details.StremioTargets.Count > 0
            ? details.StremioTargets
            : locallyEnrichedTargets;
        if (HasConflict(currentTargets, candidates))
        {
            details.AioStreamsIdentityConflict = true;
            return;
        }

        details.StremioTargets = MergeTargets(currentTargets, candidates);
    }

    internal async Task<(AioStreamsAnimeEntry? Entry, bool Unavailable)> LookupKitsuAsync(
        int kitsuId, CancellationToken cancellationToken)
    {
        if (!_options.TryGetBaseUri(out var baseUri)) return (null, true);
        var (entry, unavailable, stale) = await GetEntryAsync(baseUri, new("kitsuId", kitsuId), cancellationToken);
        return (!stale && entry?.Mappings?.KitsuId == kitsuId && HasConsistentImdbIdentity(entry)
            ? entry : null, unavailable);
    }

    internal async Task<(AioStreamsAnimeEntry? Entry, bool Unavailable)> LookupLinkedAnimeAsync(
        string provider, string externalId, CancellationToken cancellationToken)
    {
        if (!TryGetSourceIdentity(provider, externalId, out var source)) return (null, false);
        if (!_options.TryGetBaseUri(out var baseUri)) return (null, true);
        var (entry, unavailable, stale) = await GetEntryAsync(baseUri, source, cancellationToken);
        return (!stale && entry is not null && HasExactSourceIdentity(entry, source)
            && HasConsistentImdbIdentity(entry) ? entry : null, unavailable);
    }

    private async Task<(AioStreamsAnimeEntry? Entry, bool Unavailable, bool Stale)> GetEntryAsync(
        Uri baseUri,
        SourceIdentity source,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"aiostreams:anime:{baseUri.AbsoluteUri}:{source.IdType}:{source.IdValue.ToString(CultureInfo.InvariantCulture)}";
        var now = timeProvider.GetUtcNow();
        if (cache.TryGetValue<CacheEntry>(cacheKey, out var cached)
            && cached is not null
            && now - cached.FetchedAt < FreshDuration)
        {
            return (cached.Entry, false, false);
        }

        var stale = cached is not null && now - cached.FetchedAt <= StaleDuration
            ? cached.Entry
            : null;
        var failureKey = cacheKey + ":failure";
        if (cache.TryGetValue(failureKey, out bool unavailable)) return (stale, unavailable, true);

        var gateKey = cacheKey + ":gate";
        var gate = cache.GetOrCreate(gateKey, cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return new SemaphoreSlim(1, 1);
        })!;

        await gate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (cache.TryGetValue<CacheEntry>(cacheKey, out cached)
                && cached is not null
                && now - cached.FetchedAt < FreshDuration)
            {
                return (cached.Entry, false, false);
            }

            stale = cached is not null && now - cached.FetchedAt <= StaleDuration
                ? cached.Entry
                : null;
            if (cache.TryGetValue(failureKey, out unavailable)) return (stale, unavailable, true);

            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(RequestTimeout);
                var requestUri = new Uri(
                    baseUri,
                    $"api/v1/anime?idType={source.IdType}&idValue={source.IdValue.ToString(CultureInfo.InvariantCulture)}");
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await httpClientFactory.CreateClient(HttpClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
                response.EnsureSuccessStatusCode();
                var envelope = await response.Content.ReadFromJsonAsync<AioStreamsResponse>(
                    JsonOptions,
                    timeoutSource.Token);
                if (envelope?.Success != true || envelope.Data is null)
                {
                    unavailable = envelope?.Success != true;
                    RecordFailure(failureKey, unavailable);
                    return (stale, unavailable, true);
                }

                var refreshed = new CacheEntry(envelope.Data, now);
                cache.Set(
                    cacheKey,
                    refreshed,
                    new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = StaleDuration });
                cache.Remove(failureKey);
                return (refreshed.Entry, false, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException
                                              or OperationCanceledException
                                              or JsonException
                                              or NotSupportedException)
            {
                RecordFailure(failureKey, unavailable: true);
                return (stale, true, true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private void RecordFailure(string failureKey, bool unavailable)
        => cache.Set(
            failureKey,
            unavailable,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = FailureCooldown });

    private static IReadOnlyList<MediaProviderStremioTarget> GetOriginalTargets(MediaProviderTitleDetails details)
    {
        var targets = details.StremioTargets.ToList();
        if (details.StremioTarget is { } legacy
            && !targets.Any(target => HasSameIdentity(target, legacy)))
        {
            targets.Add(legacy);
        }

        return targets;
    }

    private static IReadOnlyList<MediaProviderStremioTarget> EnrichKnownKitsuTarget(
        MediaProviderTitleDetails details,
        IReadOnlyList<MediaProviderStremioTarget> targets)
    {
        if (string.Equals(details.Format, MediaFormats.Movie, StringComparison.Ordinal)) return targets;

        List<MediaProviderStremioTarget>? enriched = null;
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (!IsValidKitsuId(target.Id) || target.EpisodeMapping is not null) continue;

            enriched ??= targets.ToList();
            enriched[index] = new MediaProviderStremioTarget
            {
                Type = target.Type,
                Id = target.Id,
                EpisodeMapping = new MediaProviderStremioEpisodeMapping
                {
                    SeasonNumber = null,
                    EpisodeOffset = 0
                }
            };
        }

        return enriched ?? targets;
    }

    private static IReadOnlyList<MediaProviderStremioTarget>? BuildCandidates(
        MediaProviderTitleDetails details,
        AioStreamsAnimeEntry entry)
    {
        var mappings = entry.Mappings;
        var isMovie = string.Equals(details.Format, MediaFormats.Movie, StringComparison.Ordinal);
        if (mappings is null
            || mappings.KitsuId.HasValue && mappings.KitsuId is not > 0
            || !HasConsistentImdbIdentity(entry)
            || !isMovie && entry.Imdb?.SeasonNumber is <= 0
            || !isMovie && entry.Imdb?.FromEpisode is <= 0)
        {
            return null;
        }

        var type = isMovie ? "movie" : "series";
        var candidates = new List<MediaProviderStremioTarget>(2);
        if (mappings.KitsuId is > 0)
        {
            candidates.Add(new MediaProviderStremioTarget
            {
                Type = type,
                Id = $"kitsu:{mappings.KitsuId.Value}",
                EpisodeMapping = type == "series"
                    ? new MediaProviderStremioEpisodeMapping
                    {
                        SeasonNumber = null,
                        EpisodeOffset = 0
                    }
                    : null
            });
        }

        var imdbId = IsValidImdbId(entry.Imdb?.Id) ? entry.Imdb!.Id : mappings.ImdbId;
        if (IsValidImdbId(imdbId))
        {
            candidates.Add(new MediaProviderStremioTarget
            {
                Type = type,
                Id = imdbId!,
                EpisodeMapping = type == "series" ? GetImdbEpisodeMapping(entry.Imdb) : null
            });
        }

        return candidates;
    }

    internal static MediaProviderStremioEpisodeMapping? GetImdbEpisodeMapping(AioStreamsImdbMapping? imdb)
        => imdb is { SeasonNumber: > 0, FromEpisode: > 0 } ? new MediaProviderStremioEpisodeMapping
        {
            SeasonNumber = imdb.SeasonNumber,
            EpisodeOffset = imdb.FromEpisode.Value - 1
        } : null;

    private static bool HasConflict(
        IReadOnlyList<MediaProviderStremioTarget> currentTargets,
        IReadOnlyList<MediaProviderStremioTarget> candidates)
    {
        foreach (var current in currentTargets)
        {
            var sameProviderCandidates = candidates.Where(candidate =>
                IsKitsuProvider(current.Id) && IsKitsuProvider(candidate.Id)
                || IsImdbProvider(current.Id) && IsImdbProvider(candidate.Id)).ToList();
            if (sameProviderCandidates.Count == 0) continue;

            var candidate = sameProviderCandidates[0];
            if (!HasSameIdentity(current, candidate)
                || current.EpisodeMapping is not null
                && candidate.EpisodeMapping is not null
                && !HasSameMapping(current.EpisodeMapping, candidate.EpisodeMapping))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<MediaProviderStremioTarget> MergeTargets(
        IReadOnlyList<MediaProviderStremioTarget> currentTargets,
        IReadOnlyList<MediaProviderStremioTarget> candidates)
    {
        var merged = currentTargets.ToList();
        foreach (var candidate in candidates)
        {
            var existingIndex = merged.FindIndex(target => HasSameIdentity(target, candidate));
            if (existingIndex < 0)
            {
                merged.Add(candidate);
            }
            else if (merged[existingIndex].EpisodeMapping is null && candidate.EpisodeMapping is not null)
            {
                merged[existingIndex] = candidate;
            }
        }

        return merged;
    }

    private static bool TryGetSourceIdentity(string provider, string providerMediaId, out SourceIdentity source)
    {
        switch (provider)
        {
            case MediaObservationSiteIdentifiers.Simkl:
                if (!providerMediaId.StartsWith("anime:", StringComparison.Ordinal)
                    || !TryParsePositiveInteger(providerMediaId["anime:".Length..], out var simklId)) break;
                source = new SourceIdentity("simklId", simklId);
                return true;
            case MediaObservationSiteIdentifiers.AniList:
                if (!TryParsePositiveInteger(providerMediaId, out var aniListId)) break;
                source = new SourceIdentity("anilistId", aniListId);
                return true;
            case MediaObservationSiteIdentifiers.MyAnimeList:
                if (!providerMediaId.StartsWith("anime:", StringComparison.Ordinal)
                    || !TryParsePositiveInteger(providerMediaId["anime:".Length..], out var malId)) break;
                source = new SourceIdentity("malId", malId);
                return true;
        }

        source = default;
        return false;
    }

    private static bool HasExactSourceIdentity(AioStreamsAnimeEntry entry, SourceIdentity source)
        => source.IdType switch
        {
            "simklId" => entry.Mappings?.SimklId == source.IdValue,
            "anilistId" => entry.Mappings?.AniListId == source.IdValue,
            "malId" => entry.Mappings?.MalId == source.IdValue,
            _ => false
        };

    private static bool TryParsePositiveInteger(string value, out int parsed)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private static bool IsValidKitsuId(string value)
        => value.StartsWith("kitsu:", StringComparison.Ordinal)
           && TryParsePositiveInteger(value["kitsu:".Length..], out _);

    private static bool HasConsistentImdbIdentity(AioStreamsAnimeEntry entry)
    {
        var mappedId = entry.Mappings?.ImdbId;
        var hintedId = entry.Imdb?.Id;
        return (mappedId is null || IsValidImdbId(mappedId))
            && (hintedId is null || IsValidImdbId(hintedId))
            && (mappedId is null || hintedId is null || mappedId == hintedId);
    }

    private static bool IsValidImdbId(string? value)
        => value is { Length: > 2 }
           && value.StartsWith("tt", StringComparison.Ordinal)
           && ContainsOnlyAsciiDigits(value.AsSpan(2));

    private static bool IsKitsuProvider(string value) => value.StartsWith("kitsu:", StringComparison.Ordinal);

    private static bool IsImdbProvider(string value) => value.StartsWith("tt", StringComparison.Ordinal);

    private static bool HasSameIdentity(MediaProviderStremioTarget left, MediaProviderStremioTarget right)
        => string.Equals(left.Type, right.Type, StringComparison.Ordinal)
           && string.Equals(left.Id, right.Id, StringComparison.Ordinal);

    private static bool HasSameMapping(
        MediaProviderStremioEpisodeMapping left,
        MediaProviderStremioEpisodeMapping? right)
        => right is not null
           && left.SeasonNumber == right.SeasonNumber
           && left.EpisodeOffset == right.EpisodeOffset;

    private static bool ContainsOnlyAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9') return false;
        }

        return true;
    }

    private readonly record struct SourceIdentity(string IdType, int IdValue);

    private sealed record CacheEntry(AioStreamsAnimeEntry Entry, DateTimeOffset FetchedAt);

    private sealed class AioStreamsResponse
    {
        public bool Success { get; set; }

        public AioStreamsAnimeEntry? Data { get; set; }
    }

    internal sealed class AioStreamsAnimeEntry
    {
        public string? Type { get; set; }
        public AioStreamsMappings? Mappings { get; set; }

        public AioStreamsImdbMapping? Imdb { get; set; }
    }

    internal sealed class AioStreamsMappings
    {
        public int? SimklId { get; set; }

        [JsonPropertyName("anilistId")]
        public int? AniListId { get; set; }

        [JsonPropertyName("malId")]
        public int? MalId { get; set; }

        public int? KitsuId { get; set; }

        public string? ImdbId { get; set; }
    }

    internal sealed class AioStreamsImdbMapping
    {
        public string? Id { get; set; }

        public int? SeasonNumber { get; set; }

        public int? FromEpisode { get; set; }

        public int[]? NonImdbEpisodes { get; set; }
    }
}
