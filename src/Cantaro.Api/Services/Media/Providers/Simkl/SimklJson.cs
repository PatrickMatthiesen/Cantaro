using System.Globalization;
using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

internal readonly record struct SimklIdentity(string Type, int Id)
{
    public string ProviderMediaId => $"{Type}:{Id}";
}

internal static class SimklJson
{
    public static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;

    public static string? String(JsonElement value, string name)
    {
        var property = Property(value, name);
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    public static int? Int(JsonElement value, string name)
    {
        var text = String(value, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    public static DateTimeOffset? Date(JsonElement value, string name)
        => DateTimeOffset.TryParse(String(value, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    public static IEnumerable<JsonElement> Array(JsonElement value)
        => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    public static SimklIdentity ParseId(string id)
    {
        var parts = id.Split(':', 2);
        if (parts.Length != 2 || parts[0] is not ("tv" or "movie" or "anime")
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            throw new InvalidOperationException($"SIMKL media ID '{id}' is invalid.");
        return new SimklIdentity(parts[0], number);
    }

    public static string ListType(string type) => type switch { "tv" => "shows", "movie" => "movies", "anime" => "anime", _ => throw new ArgumentOutOfRangeException(nameof(type)) };
    public static string MediaKind(string type) => type switch { "tv" => MediaKinds.Series, "movie" => MediaKinds.Movie, _ => MediaKinds.Anime };
    public static string ProgressDimension(string type) => type == "movie" ? MediaProgressDimensions.CompletionOnly : MediaProgressDimensions.Episode;

    public static IReadOnlyList<MediaProviderLibraryItem> MapLibraryItems(JsonElement response)
    {
        var items = new List<MediaProviderLibraryItem>();
        foreach (var (container, type, member) in new[] { ("shows", "tv", "show"), ("movies", "movie", "movie"), ("anime", "anime", "show") })
        {
            foreach (var row in Array(Property(response, container)))
            {
                var media = Property(row, member);
                if (type == "anime" && media.ValueKind != JsonValueKind.Object)
                    media = Property(row, "anime");
                var ids = Property(media, "ids");
                var id = Int(ids, "simkl") ?? Int(ids, "simkl_id");
                if (id is not > 0) continue;
                var title = String(media, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;
                var watched = type == "movie" ? null : ReadWatched(row, type);
                var count = type == "movie" ? null : Int(row, "watched_episodes_count");
                var total = type == "movie" ? null : Int(row, "total_episodes_count");
                var status = MapStatus(String(row, "status"));
                items.Add(new MediaProviderLibraryItem
                {
                    ProviderMediaId = $"{type}:{id}",
                    ProviderLibraryEntryId = $"{type}:{id}",
                    Title = title,
                    MediaKind = MediaKind(type),
                    Format = type switch { "tv" => MediaFormats.Tv, "movie" => MediaFormats.Movie,
                        _ => MapAnimeFormat(String(row, "anime_type") ?? String(media, "anime_type")) },
                    PosterUrl = Image(String(media, "poster"), "posters", "_m.webp"),
                    BackgroundUrl = Image(String(media, "fanart"), "fanart", "_medium.webp"),
                    ExternalUrl = ExternalUrl(type, id.Value),
                    CrossReferences = CrossReferences(type, ids),
                    StartYear = Int(media, "year"),
                    EpisodeCount = total,
                    ReleasedCount = total is { } t && Int(row, "not_aired_episodes_count") is { } remaining
                        ? Math.Max(0, t - remaining) : null,
                    TotalKnownCount = total,
                    Status = status,
                    Score = Int(row, "user_rating") is { } rating ? rating * 10m : null,
                    ProgressEpisodes = type == "tv" ? 0 : type == "anime" ? ContiguousAnimePrefix(watched) : null,
                    WatchedEpisodes = watched,
                    HasNonContiguousProgress = type == "tv" ? count is > 0 : HasGaps(watched, count, type),
                    PrimaryProgressDimension = ProgressDimension(type),
                    ReleaseStatusDimension = ProgressDimension(type),
                    LastRemoteUpdateAt = new[] { Date(row, "last_watched_at"), Date(row, "added_to_watchlist_at"),
                        Date(row, "user_rated_at"), Date(row, "updated_at") }.Max()
                });
            }
        }
        return items;
    }

    public static HashSet<string> MapIds(JsonElement response)
    {
        var result = new HashSet<string>();
        foreach (var (container, type, member) in new[] { ("shows", "tv", "show"), ("movies", "movie", "movie"), ("anime", "anime", "show") })
            foreach (var row in Array(Property(response, container)))
            {
                var ids = Property(Property(row, member), "ids");
                if (type == "anime" && ids.ValueKind != JsonValueKind.Object)
                    ids = Property(Property(row, "anime"), "ids");
                if (ids.ValueKind != JsonValueKind.Object) ids = Property(row, "ids");
                var id = Int(ids, "simkl") ?? Int(ids, "simkl_id");
                if (id is > 0) result.Add($"{type}:{id}");
            }
        return result;
    }

    public static MediaProviderSearchResult? MapSearchResult(JsonElement media, string kind)
    {
        var ids = Property(media, "ids");
        var id = Int(ids, "simkl") ?? Int(ids, "simkl_id");
        var title = String(media, "title_en") ?? String(media, "title");
        if (id is not > 0 || string.IsNullOrWhiteSpace(title)) return null;
        var type = kind switch { MediaKinds.Series => "tv", MediaKinds.Movie => "movie", _ => "anime" };
        return new MediaProviderSearchResult
        {
            ProviderId = "simkl", ProviderMediaId = $"{type}:{id}", Title = title,
            NativeTitle = String(media, "title_romaji"),
            Synonyms = Array(Property(media, "all_titles")).Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).Where(x => !string.Equals(x, title, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray(),
            MediaKind = kind, PosterUrl = Image(String(media, "poster"), "posters", "_m.webp"),
            StartYear = Int(media, "year"), EpisodeCount = Int(media, "ep_count"),
            PrimaryProgressDimension = ProgressDimension(type), ReleaseStatusDimension = ProgressDimension(type)
        };
    }

    public static MediaProviderTitleDetails MapDetails(JsonElement media, string type, int id)
    {
        var ids = Property(media, "ids");
        var title = String(media, "title_en") ?? String(media, "title") ?? "Unknown title";
        var total = Int(media, "total_episodes") ?? Int(media, "ep_count");
        var release = String(media, "status");
        return new MediaProviderTitleDetails
        {
            ProviderId = "simkl", ProviderMediaId = $"{type}:{id}", Title = title,
            NativeTitle = String(media, "title_romaji"),
            Synonyms = Array(Property(media, "all_titles")).Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).Where(x => !string.Equals(x, title, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray(),
            MediaKind = MediaKind(type), Synopsis = String(media, "overview"),
            Format = type switch { "tv" => MediaFormats.Tv, "movie" => MediaFormats.Movie, _ => MapAnimeFormat(String(media, "anime_type")) },
            PosterUrl = Image(String(media, "poster"), "posters", "_m.webp"),
            BackgroundUrl = Image(String(media, "fanart"), "fanart", "_medium.webp"),
            CrossReferences = CrossReferences(type, ids), StartYear = Int(media, "year"),
            EpisodeCount = total, TotalKnownCount = total,
            ReleasedCount = release is "ended" or "released" ? total : null,
            PrimaryProgressDimension = ProgressDimension(type), ReleaseStatusDimension = ProgressDimension(type)
        };
    }

    private static IReadOnlyList<MediaProviderWatchedEpisode>? ReadWatched(JsonElement row, string type)
    {
        var seasons = Property(row, "seasons");
        if (seasons.ValueKind != JsonValueKind.Array) return null;
        var episodes = new List<MediaProviderWatchedEpisode>();
        foreach (var season in Array(seasons))
        {
            var seasonNumber = Int(season, "number") ?? (type == "anime" ? 1 : null);
            foreach (var episode in Array(Property(season, "episodes")))
            {
                var episodeNumber = Int(episode, "number");
                if (episodeNumber is not > 0 || seasonNumber is null or < 0) continue;
                episodes.Add(new MediaProviderWatchedEpisode
                {
                    SeasonNumber = seasonNumber, EpisodeNumber = episodeNumber.Value,
                    ProviderEpisodeId = String(Property(episode, "ids"), "simkl_id") ?? String(Property(episode, "ids"), "simkl"),
                    WatchedAt = Date(episode, "watched_at")
                });
            }
        }
        return episodes.DistinctBy(EpisodeKey).ToArray();
    }

    public static IReadOnlyList<MediaProviderWatchedEpisode> MapEpisodeCatalog(JsonElement response, string type)
        => MapEpisodeCatalogSnapshot(response, type).RegularEpisodes;

    public static MediaProviderEpisodeCatalogSnapshot MapEpisodeCatalogSnapshot(JsonElement response, string type)
    {
        var regularEpisodes = new List<MediaProviderWatchedEpisode>();
        var specials = new List<MediaProviderWatchedEpisode>();
        foreach (var row in Array(response))
        {
            var isSpecial = String(row, "type") == "special" || Int(row, "season") == 0;
            var season = isSpecial ? 0 : Int(row, "season") ?? (type == "anime" ? 1 : null);
            var number = Int(row, "episode");
            if (number is not > 0 || season is null || (!isSpecial && season <= 0)) continue;
            var episode = new MediaProviderWatchedEpisode
            {
                SeasonNumber = isSpecial ? 0 : season,
                EpisodeNumber = number.Value,
                ProviderEpisodeId = String(Property(row, "ids"), "simkl_id"),
                Title = String(row, "title")
            };
            (isSpecial ? specials : regularEpisodes).Add(episode);
        }
        return new MediaProviderEpisodeCatalogSnapshot
        {
            RegularEpisodes = regularEpisodes
                .OrderBy(x => x.SeasonNumber)
                .ThenBy(x => x.EpisodeNumber)
                .DistinctBy(EpisodeKey)
                .ToArray(),
            Specials = specials
                .OrderBy(x => x.EpisodeNumber)
                .DistinctBy(x => x.EpisodeNumber)
                .ToArray()
        };
    }

    private static bool HasGaps(IReadOnlyList<MediaProviderWatchedEpisode>? watched, int? count, string type)
    {
        if (type == "movie") return false;
        if (watched is null) return count is > 0;
        if (count is { } total && watched.Count != total) return true;
        var sorted = watched.OrderBy(x => x.SeasonNumber).ThenBy(x => x.EpisodeNumber).ToArray();
        if (sorted.Length == 0) return false;
        if (sorted[0].SeasonNumber != 1 || sorted[0].EpisodeNumber != 1) return true;
        for (var i = 1; i < sorted.Length; i++)
            if (sorted[i].SeasonNumber == sorted[i - 1].SeasonNumber && sorted[i].EpisodeNumber != sorted[i - 1].EpisodeNumber + 1)
                return true;
        return false;
    }

    private static int ContiguousAnimePrefix(IReadOnlyList<MediaProviderWatchedEpisode>? watched)
    {
        if (watched is null) return 0;
        var regular = watched.Where(x => x.SeasonNumber == 1).Select(x => x.EpisodeNumber).ToHashSet();
        var count = 0;
        while (regular.Contains(count + 1)) count++;
        return count;
    }

    public static void ApplyCatalog(MediaProviderLibraryItem item, IReadOnlyList<MediaProviderWatchedEpisode> catalog)
    {
        if (item.WatchedEpisodes is null)
        {
            var hasProgress = item.HasNonContiguousProgress;
            item.ProgressEpisodes = 0;
            item.HasNonContiguousProgress = hasProgress;
            return;
        }
        var watched = item.WatchedEpisodes.Select(EpisodeKey).ToHashSet();
        var prefix = 0;
        while (prefix < catalog.Count && watched.Contains(EpisodeKey(catalog[prefix]))) prefix++;
        item.ProgressEpisodes = prefix;
        item.HasNonContiguousProgress = item.WatchedEpisodes.Count != prefix;
    }

    public static string EpisodeKey(MediaProviderWatchedEpisode episode)
        => $"{episode.SeasonNumber ?? 1}:{episode.EpisodeNumber}";

    public static object EpisodeEnvelope(SimklIdentity id, IReadOnlyCollection<MediaProviderWatchedEpisode> episodes)
    {
        if (id.Type == "anime")
        {
            if (episodes.Any(x => x.SeasonNumber is not (null or 1)))
                throw new InvalidOperationException("SIMKL anime specials cannot be written by regular episode number.");
            return Envelope(id.Type, new { ids = new { simkl = id.Id },
                episodes = episodes.Select(x => new { number = x.EpisodeNumber }).ToArray() });
        }
        var seasons = episodes.GroupBy(x => x.SeasonNumber ?? 1)
            .Select(x => new { number = x.Key, episodes = x.Select(y => new { number = y.EpisodeNumber }).ToArray() }).ToArray();
        return Envelope(id.Type, new { ids = new { simkl = id.Id }, seasons });
    }

    public static object Envelope(string type, object item) => ListType(type) switch
    {
        "shows" => new { shows = new[] { item } },
        "movies" => new { movies = new[] { item } },
        _ => new { shows = new[] { item } }
    };

    public static string ToStatus(string status, string type) => status switch
    {
        MediaLibraryStatuses.Current => type == "movie" ? "completed" : "watching",
        MediaLibraryStatuses.Planned => "plantowatch",
        MediaLibraryStatuses.Paused => type == "movie" ? "plantowatch" : "hold",
        MediaLibraryStatuses.Completed => "completed",
        MediaLibraryStatuses.Dropped => "dropped",
        MediaLibraryStatuses.Repeating => type == "movie" ? "completed" : "watching",
        _ => throw new InvalidOperationException($"Media status '{status}' cannot be written to SIMKL.")
    };

    public static string MapStatus(string? status) => status switch
    {
        "watching" => MediaLibraryStatuses.Current,
        "plantowatch" => MediaLibraryStatuses.Planned,
        "hold" => MediaLibraryStatuses.Paused,
        "completed" => MediaLibraryStatuses.Completed,
        "dropped" => MediaLibraryStatuses.Dropped,
        _ => MediaLibraryStatuses.Unknown
    };

    public static string? ResolvedHistoryStatus(JsonElement response)
    {
        var statuses = Property(Property(response, "added"), "statuses");
        foreach (var row in Array(statuses))
            if (String(Property(row, "response"), "status") is { } status) return MapStatus(status);
        return null;
    }

    public static string? ResolvedListStatus(JsonElement response, string type)
    {
        foreach (var row in Array(Property(Property(response, "added"), type == "anime" ? "shows" : ListType(type))))
            if (String(row, "to") is { } status) return status;
        return null;
    }

    public static void ThrowIfNotFound(JsonElement response, string type)
    {
        var missing = Property(response, "not_found");
        if (Array(Property(missing, type == "anime" ? "shows" : ListType(type))).Any()
            || Array(Property(missing, "episodes")).Any())
            throw new InvalidOperationException("SIMKL could not resolve the requested title or episode.");
    }

    private static IReadOnlyList<MediaProviderCrossReference> CrossReferences(string type, JsonElement ids)
    {
        if (type != "anime") return [];
        var references = new List<MediaProviderCrossReference>();
        if (String(ids, "mal") is { Length: > 0 } mal)
            references.Add(new MediaProviderCrossReference { ProviderId = "myanimelist", ProviderMediaId = $"anime:{mal}", ExternalUrl = $"https://myanimelist.net/anime/{mal}" });
        if (String(ids, "anilist") is { Length: > 0 } anilist)
            references.Add(new MediaProviderCrossReference { ProviderId = "anilist", ProviderMediaId = anilist, ExternalUrl = $"https://anilist.co/anime/{anilist}" });
        return references;
    }

    private static string? MapAnimeFormat(string? value) => value?.ToLowerInvariant() switch
    {
        "tv" => MediaFormats.Tv, "movie" => MediaFormats.Movie, "ova" => MediaFormats.Ova,
        "ona" => MediaFormats.Ona, "special" => MediaFormats.Special, "music video" or "music" => MediaFormats.Music,
        _ => null
    };

    private static string ExternalUrl(string type, int id)
        => $"https://simkl.com/{(type == "movie" ? "movies" : type)}/{id}/";

    private static string? Image(string? path, string category, string size)
        => string.IsNullOrWhiteSpace(path) ? null
            : $"https://simkl.in/{category}/{path.TrimStart('/')}{size}";
}
