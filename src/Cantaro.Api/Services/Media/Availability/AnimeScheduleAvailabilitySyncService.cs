using System.Globalization;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class AnimeScheduleAvailabilitySyncService(
    ApplicationDbContext dbContext,
    AnimeScheduleApiClient apiClient,
    ILogger<AnimeScheduleAvailabilitySyncService> logger)
{
    private const int AnimeSchedulePageSize = 18;
    private const string EnglishLanguageCode = "en";
    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly AnimeScheduleApiClient _apiClient = apiClient;
    private readonly ILogger<AnimeScheduleAvailabilitySyncService> _logger = logger;

    public Task<int> SyncUserLibraryAsync(int userId, CancellationToken cancellationToken) =>
        SyncLibrariesAsync(userId, cancellationToken);

    public Task<int> SyncAllLibrariesAsync(CancellationToken cancellationToken) =>
        SyncLibrariesAsync(null, cancellationToken);

    private async Task<int> SyncLibrariesAsync(int? userId, CancellationToken cancellationToken)
    {
        if (!_apiClient.IsConfigured)
        {
            return 0;
        }

        await SyncGate.WaitAsync(cancellationToken);
        try
        {
            var linkedTitles = await LoadLinkedTitlesAsync(userId, cancellationToken);
            return await SyncLinkedTitlesAsync(linkedTitles, userId, cancellationToken);
        }
        finally
        {
            SyncGate.Release();
        }
    }

    private async Task<List<LinkedTitle>> LoadLinkedTitlesAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        var entries = _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(entry => entry.MediaTitle!.MediaKind == MediaKinds.Anime);
        if (userId is not null)
        {
            entries = entries.Where(entry => entry.UserId == userId.Value);
        }

        return await entries
            .SelectMany(entry => entry.MediaTitle!.ProviderLinks)
            .Where(link => link.Provider == "anilist")
            .Select(link => new LinkedTitle(link.MediaTitleId, link.ExternalId))
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<int> SyncLinkedTitlesAsync(
        IReadOnlyCollection<LinkedTitle> linkedTitles,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (linkedTitles.Count == 0)
        {
            return 0;
        }

        var titleIds = linkedTitles.Select(title => title.MediaTitleId).Distinct().ToArray();
        var knownEpisodeCounts = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(title => titleIds.Contains(title.Id))
            .Select(title => new { title.Id, title.EpisodeCount })
            .ToDictionaryAsync(title => title.Id, title => title.EpisodeCount, cancellationToken);
        var episodes = await _dbContext.MediaEpisodes
            .Where(episode => titleIds.Contains(episode.MediaTitleId))
            .ToListAsync(cancellationToken);

        var schedulesByRoute = await LoadSchedulesAsync(
            linkedTitles,
            knownEpisodeCounts,
            cancellationToken);
        if (schedulesByRoute.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var releasedCounts = ReadExistingTrackCounts(episodes);
        ApplyFinishedTitleCounts(schedulesByRoute.Values, releasedCounts);

        if (schedulesByRoute.Values.Any(schedule => !IsFinished(schedule.Anime)))
        {
            var currentTimetable = await _apiClient.GetTimetableAsync(null, null, cancellationToken);
            ApplyTimetableCounts(currentTimetable, schedulesByRoute, releasedCounts, now);

            await ResolveMissingRunningTrackCountsAsync(
                schedulesByRoute,
                releasedCounts,
                now,
                cancellationToken);
        }

        var changed = ApplyReleasedCounts(releasedCounts, schedulesByRoute, episodes, now);
        if (changed > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Updated language availability on {EpisodeCount} episodes from AnimeSchedule for {SyncScope}.",
            changed,
            userId is null ? "all libraries" : $"user {userId.Value}");
        return changed;
    }

    private async Task<Dictionary<string, TitleSchedule>> LoadSchedulesAsync(
        IReadOnlyCollection<LinkedTitle> linkedTitles,
        IReadOnlyDictionary<Guid, int?> knownEpisodeCounts,
        CancellationToken cancellationToken)
    {
        var schedulesByRoute = new Dictionary<string, TitleSchedule>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in linkedTitles.Chunk(AnimeSchedulePageSize))
        {
            var page = await _apiClient.GetAnimeByAniListIdsAsync(
                chunk.Select(title => title.AniListId).ToArray(),
                cancellationToken);
            foreach (var anime in page?.Anime ?? [])
            {
                var aniListId = ReadAniListId(anime.Websites.AniList);
                var linkedTitle = aniListId is null
                    ? null
                    : chunk.FirstOrDefault(title => title.AniListId == aniListId);
                if (linkedTitle is not null)
                {
                    schedulesByRoute[anime.Route] = new TitleSchedule(
                        linkedTitle.MediaTitleId,
                        anime,
                        knownEpisodeCounts.GetValueOrDefault(linkedTitle.MediaTitleId));
                }
            }
        }

        return schedulesByRoute;
    }

    private static Dictionary<TrackKey, int> ReadExistingTrackCounts(
        IEnumerable<MediaEpisode> episodes)
    {
        var counts = new Dictionary<TrackKey, int>();
        foreach (var episode in episodes)
        {
            if (episode.AvailableSubtitleLanguageCodes.Contains(EnglishLanguageCode, StringComparer.OrdinalIgnoreCase))
            {
                SetMaximum(counts, new TrackKey(episode.MediaTitleId, "sub"), episode.EpisodeNumber);
            }

            if (episode.AvailableAudioLanguageCodes.Contains(EnglishLanguageCode, StringComparer.OrdinalIgnoreCase))
            {
                SetMaximum(counts, new TrackKey(episode.MediaTitleId, "dub"), episode.EpisodeNumber);
            }
        }

        return counts;
    }

    private static void ApplyFinishedTitleCounts(
        IEnumerable<TitleSchedule> schedules,
        IDictionary<TrackKey, int> releasedCounts)
    {
        foreach (var schedule in schedules.Where(schedule => IsFinished(schedule.Anime)))
        {
            var episodeCount = ResolveEpisodeCount(schedule);
            if (episodeCount is null or <= 0)
            {
                continue;
            }

            if (TrackExists(schedule.Anime, "sub"))
            {
                SetMaximum(releasedCounts, new TrackKey(schedule.MediaTitleId, "sub"), episodeCount.Value);
            }

            if (TrackExists(schedule.Anime, "dub"))
            {
                SetMaximum(releasedCounts, new TrackKey(schedule.MediaTitleId, "dub"), episodeCount.Value);
            }
        }
    }

    private async Task ResolveMissingRunningTrackCountsAsync(
        IReadOnlyDictionary<string, TitleSchedule> schedulesByRoute,
        IDictionary<TrackKey, int> releasedCounts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var unresolved = schedulesByRoute.Values
            .Where(schedule => !IsFinished(schedule.Anime))
            .SelectMany(schedule => new[] { "sub", "dub" }
                .Where(track => TrackExists(schedule.Anime, track))
                .Select(track => new
                {
                    Key = new TrackKey(schedule.MediaTitleId, track),
                    Schedule = schedule,
                    SearchStart = ResolveTrackSearchStart(schedule.Anime, track, now)
                }))
            .Where(item => !releasedCounts.ContainsKey(item.Key))
            .ToDictionary(item => item.Key, item => (item.Schedule, item.SearchStart));
        if (unresolved.Count == 0)
        {
            return;
        }

        var currentWeekStart = StartOfIsoWeek(now.UtcDateTime.Date);
        for (var cursor = currentWeekStart.AddDays(-7);
             unresolved.Count > 0 && unresolved.Values.Any(item => cursor >= item.SearchStart.UtcDateTime.Date);
             cursor = cursor.AddDays(-7))
        {
            var year = ISOWeek.GetYear(cursor);
            var week = ISOWeek.GetWeekOfYear(cursor);
            var timetable = await _apiClient.GetTimetableAsync(year, week, cancellationToken);
            ApplyTimetableCounts(timetable, schedulesByRoute, releasedCounts, now);

            foreach (var key in unresolved.Keys.ToArray())
            {
                if (releasedCounts.ContainsKey(key)
                    || cursor < unresolved[key].SearchStart.UtcDateTime.Date)
                {
                    unresolved.Remove(key);
                }
            }
        }
    }

    private static void ApplyTimetableCounts(
        IReadOnlyList<AnimeScheduleTimetableEntry>? timetable,
        IReadOnlyDictionary<string, TitleSchedule> schedulesByRoute,
        IDictionary<TrackKey, int> releasedCounts,
        DateTimeOffset now)
    {
        foreach (var entry in timetable ?? [])
        {
            if (!schedulesByRoute.TryGetValue(entry.Route, out var schedule)
                || entry.EpisodeNumber <= 0
                || entry.AirType is not ("sub" or "dub"))
            {
                continue;
            }

            var releasedThrough = ResolveReleasedThrough(entry, now);
            var episodeCount = ResolveEpisodeCount(schedule);
            if (episodeCount is > 0)
            {
                releasedThrough = Math.Min(releasedThrough, episodeCount.Value);
            }

            if (releasedThrough > 0)
            {
                SetMaximum(
                    releasedCounts,
                    new TrackKey(schedule.MediaTitleId, entry.AirType),
                    releasedThrough);
            }
        }
    }

    private int ApplyReleasedCounts(
        IReadOnlyDictionary<TrackKey, int> releasedCounts,
        IReadOnlyDictionary<string, TitleSchedule> schedulesByRoute,
        ICollection<MediaEpisode> episodes,
        DateTimeOffset now)
    {
        var schedulesByTitle = schedulesByRoute.Values
            .DistinctBy(schedule => schedule.MediaTitleId)
            .ToDictionary(schedule => schedule.MediaTitleId);
        var existingByTitle = episodes
            .GroupBy(episode => episode.MediaTitleId)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(episode => episode.EpisodeNumber));
        var changed = 0;

        foreach (var (track, releasedThrough) in releasedCounts)
        {
            if (!schedulesByTitle.ContainsKey(track.MediaTitleId))
            {
                continue;
            }

            if (!existingByTitle.TryGetValue(track.MediaTitleId, out var existing))
            {
                existing = [];
                existingByTitle[track.MediaTitleId] = existing;
            }

            for (var episodeNumber = 1; episodeNumber <= releasedThrough; episodeNumber++)
            {
                if (!existing.TryGetValue(episodeNumber, out var episode))
                {
                    episode = new MediaEpisode
                    {
                        Id = Guid.NewGuid(),
                        MediaTitleId = track.MediaTitleId,
                        EpisodeNumber = episodeNumber,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _dbContext.MediaEpisodes.Add(episode);
                    episodes.Add(episode);
                    existing[episodeNumber] = episode;
                }

                var languageCodes = track.AirType == "dub"
                    ? episode.AvailableAudioLanguageCodes
                    : episode.AvailableSubtitleLanguageCodes;
                if (languageCodes.Contains(EnglishLanguageCode, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (track.AirType == "dub")
                {
                    episode.AvailableAudioLanguageCodes = [.. languageCodes, EnglishLanguageCode];
                }
                else
                {
                    episode.AvailableSubtitleLanguageCodes = [.. languageCodes, EnglishLanguageCode];
                }

                episode.UpdatedAt = now;
                changed++;
            }
        }

        return changed;
    }

    private static int ResolveReleasedThrough(
        AnimeScheduleTimetableEntry entry,
        DateTimeOffset now)
    {
        if (string.Equals(entry.AiringStatus, "aired", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.AiringStatus, "airing", StringComparison.OrdinalIgnoreCase))
        {
            return entry.EpisodeNumber;
        }

        if (string.Equals(entry.AiringStatus, "unaired", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.AiringStatus, "delayed-air", StringComparison.OrdinalIgnoreCase))
        {
            return entry.EpisodeNumber - 1;
        }

        return entry.EpisodeDate <= now
            ? entry.EpisodeNumber
            : entry.EpisodeNumber - 1;
    }

    private static bool IsFinished(AnimeScheduleAnime anime) =>
        string.Equals(anime.Status, "Finished", StringComparison.OrdinalIgnoreCase);

    private static bool TrackExists(AnimeScheduleAnime anime, string airType)
    {
        var trackPremier = airType == "dub" ? anime.DubPremier : anime.SubPremier;
        if (IsMeaningfulDate(trackPremier))
        {
            return true;
        }

        var trackTime = airType == "dub" ? anime.DubTime : anime.SubTime;
        return trackTime.TimeOfDay != TimeSpan.Zero
            && trackTime.TimeOfDay != anime.JpnTime.TimeOfDay;
    }

    private static DateTimeOffset ResolveTrackSearchStart(
        AnimeScheduleAnime anime,
        string airType,
        DateTimeOffset now)
    {
        var trackPremier = airType == "dub" ? anime.DubPremier : anime.SubPremier;
        if (IsMeaningfulDate(trackPremier))
        {
            return trackPremier;
        }

        return IsMeaningfulDate(anime.Premier)
            ? anime.Premier
            : now.AddYears(-10);
    }

    private static bool IsMeaningfulDate(DateTimeOffset value) => value.Year > 1;

    private static int? ResolveEpisodeCount(TitleSchedule schedule) =>
        schedule.Anime.Episodes is > 0
            ? schedule.Anime.Episodes
            : schedule.KnownEpisodeCount;

    private static DateTime StartOfIsoWeek(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    private static void SetMaximum(
        IDictionary<TrackKey, int> counts,
        TrackKey key,
        int value)
    {
        if (!counts.TryGetValue(key, out var existing) || value > existing)
        {
            counts[key] = value;
        }
    }

    private static string? ReadAniListId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var normalizedUrl = url.Contains("://", StringComparison.Ordinal)
            ? url
            : $"https://{url.TrimStart('/')}";
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "anilist.co", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var animeIndex = Array.FindIndex(segments, segment =>
            string.Equals(segment, "anime", StringComparison.OrdinalIgnoreCase));
        return animeIndex >= 0 && animeIndex + 1 < segments.Length
            ? segments[animeIndex + 1]
            : null;
    }

    private sealed record LinkedTitle(Guid MediaTitleId, string AniListId);

    private sealed record TitleSchedule(
        Guid MediaTitleId,
        AnimeScheduleAnime Anime,
        int? KnownEpisodeCount);

    private readonly record struct TrackKey(Guid MediaTitleId, string AirType);
}
