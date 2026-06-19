using System.Collections.Concurrent;

namespace Cantaro.Api.Services;

public sealed record MusicSyncThrottleStatus(int Usage, int Remaining, bool CanSyncNow, string? Message);

public sealed class MusicSyncThrottleService
{
    public const int SongSyncLimitPerWindow = 2000;
    public const int SyncWindowMinutes = 10;
    private const int MaxSongsCountedPerSyncRun = SongSyncLimitPerWindow * 2;

    private sealed class Bucket
    {
        public object SyncRoot { get; } = new();
        public List<UsageEvent> Events { get; } = [];
    }

    private sealed record UsageEvent(DateTimeOffset OccurredAt, int SongsSynced);

    private readonly ConcurrentDictionary<string, Bucket> _usageByUserAndService = new();

    public MusicSyncThrottleStatus GetStatus(int userId, string service, DateTimeOffset now)
    {
        var usage = GetSongsSyncedInWindow(userId, service, now);
        var remaining = Math.Max(0, SongSyncLimitPerWindow - usage);
        var canSyncNow = usage <= 0 || remaining > 0;

        string? message = null;
        if (usage > 0 && remaining <= 0)
        {
            message = $"Song sync limit reached ({SongSyncLimitPerWindow} songs per {SyncWindowMinutes} minutes).";
        }
        else if (usage > 0)
        {
            message = $"You can sync up to {remaining} more songs in the current {SyncWindowMinutes}-minute window.";
        }

        return new MusicSyncThrottleStatus(usage, remaining, canSyncNow, message);
    }

    public void AddUsage(int userId, string service, int songsSynced, DateTimeOffset now)
    {
        songsSynced = Math.Min(songsSynced, MaxSongsCountedPerSyncRun);
        if (songsSynced <= 0) return;

        var bucket = _usageByUserAndService.GetOrAdd(BuildKey(userId, service), _ => new Bucket());
        var cutoff = now.AddMinutes(-SyncWindowMinutes);
        lock (bucket.SyncRoot)
        {
            bucket.Events.RemoveAll(item => item.OccurredAt < cutoff);
            bucket.Events.Add(new UsageEvent(now, songsSynced));
        }
    }

    private int GetSongsSyncedInWindow(int userId, string service, DateTimeOffset now)
    {
        var bucket = _usageByUserAndService.GetOrAdd(BuildKey(userId, service), _ => new Bucket());
        var cutoff = now.AddMinutes(-SyncWindowMinutes);
        lock (bucket.SyncRoot)
        {
            bucket.Events.RemoveAll(item => item.OccurredAt < cutoff);
            return bucket.Events.Sum(item => item.SongsSynced);
        }
    }

    private static string BuildKey(int userId, string service) => $"{userId}:{service}";
}

