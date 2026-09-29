using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services.Spotify;

/// <summary>
/// Reuses successful catalogue queries across workers, playlists and restarts.
/// Only application catalogue-token searches may use this cache: user-token search
/// results can depend on that user's market and must not be shared here.
/// </summary>
public sealed class SpotifySearchCache(IServiceScopeFactory scopes, TimeProvider clock)
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 128)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private long _nextCleanupTicks;

    public async Task<IReadOnlyList<SpotifyTrackSnapshot>> GetOrSearchAsync(
        string query, int limit,
        Func<CancellationToken, Task<IReadOnlyList<SpotifyTrackSnapshot>>> search,
        CancellationToken ct)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"catalog-v1\n{Math.Clamp(limit, 1, 50)}\n{query}"));
        var key = Convert.ToHexString(hash);
        // Coalesce simultaneous matching workers in this process; the database
        // retains completed queries for other processes and future runs.
        var gate = Gates[hash[0] % Gates.Length];
        await gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entry = await db.SpotifySearchCacheEntries.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, ct);
            if (entry is not null && entry.ExpiresAt > clock.GetUtcNow())
                return JsonSerializer.Deserialize<SpotifyTrackSnapshot[]>(entry.ResultsJson)!;

            // Save each completed query independently of the matching job's
            // transaction. Failed/cancelled requests never replace stored results.
            var results = await search(ct);
            var json = JsonSerializer.Serialize(results);
            var now = clock.GetUtcNow();
            var expiresAt = now.Add(results.Count == 0 ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7));
            // A completed HTTP response is useful even if the playlist job was
            // cancelled just afterwards. Bound this independent persistence step.
            using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SpotifySearchCacheEntries" ("Key", "ResultsJson", "ExpiresAt")
                VALUES ({key}, {json}, {expiresAt})
                ON CONFLICT ("Key") DO UPDATE SET
                    "ResultsJson" = EXCLUDED."ResultsJson", "ExpiresAt" = EXCLUDED."ExpiresAt"
                """, persistence.Token);
            var cleanupTicks = Volatile.Read(ref _nextCleanupTicks);
            if (cleanupTicks <= now.UtcTicks && Interlocked.CompareExchange(
                ref _nextCleanupTicks, now.AddHours(1).UtcTicks, cleanupTicks) == cleanupTicks)
            {
                // Upsert also handles a refresh racing another process's expiry
                // cleanup; no tracked expired row is updated after deletion.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""DELETE FROM "SpotifySearchCacheEntries" WHERE "ExpiresAt" <= {now}""", persistence.Token);
            }
            ct.ThrowIfCancellationRequested();
            return results;
        }
        finally { gate.Release(); }
    }
}
