using System.Net;
using System.Net.Http.Headers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services.Spotify;

/// <summary>
/// Coordinates Spotify Web API pacing and rate limits across app instances.
/// Production instances keep this state in the shared application database.
/// </summary>
public sealed class SpotifyCooldown
{
    private static readonly TimeSpan DatabasePollInterval = TimeSpan.FromMilliseconds(250);
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SpotifyRequestGateOptions _options;
    private readonly Lock _memoryLock = new();
    private DateTimeOffset _memoryNotBefore = DateTimeOffset.MinValue;
    private bool _memoryQuotaCooldown;

    /// <summary>Test compatibility constructor. Production DI uses the durable constructor.</summary>
    public SpotifyCooldown(TimeProvider timeProvider)
        : this(null, timeProvider, new SpotifyRequestGateOptions { RequestInterval = TimeSpan.Zero })
    {
    }

    public SpotifyCooldown(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<SpotifyRequestGateOptions> options)
        : this(scopeFactory, timeProvider, options.Value)
    {
    }

    internal SpotifyCooldown(
        IServiceScopeFactory? scopeFactory,
        TimeProvider timeProvider,
        SpotifyRequestGateOptions options)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>Legacy in-memory deadline for tests that use the compatibility constructor.</summary>
    public DateTimeOffset NotBefore
    {
        get
        {
            lock (_memoryLock) return _memoryNotBefore;
        }
    }

    /// <summary>Legacy synchronous check. Production request paths should use AcquireAsync.</summary>
    public void ThrowIfActive()
    {
        lock (_memoryLock)
        {
            var remaining = _memoryNotBefore - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                throw CreateRateLimitException(remaining, _memoryQuotaCooldown);
        }
    }

    /// <summary>Legacy in-memory recording retained for focused unit tests.</summary>
    public TimeSpan Record(RetryConditionHeaderValue? retryAfter)
        => RecordMemory(retryAfter, isQuotaExceeded: false);

    /// <summary>
    /// Waits only for the small request-pacing interval. An active Spotify cooldown
    /// fails immediately so a worker can persist and schedule the retry.
    /// </summary>
    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        ThrowIfLocallyActive();
        if (_scopeFactory is null)
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _timeProvider.GetUtcNow();
            var state = await ReadStateAsync(cancellationToken);
            RememberDeadline(state.NotBefore, state.NotBeforeIsQuotaExceeded);
            ThrowIfLocallyActive();
            var cooldownRemaining = state.NotBefore - now;
            if (cooldownRemaining > TimeSpan.Zero)
                throw CreateRateLimitException(cooldownRemaining, state.NotBeforeIsQuotaExceeded);

            var pacingRemaining = state.NextRequestAt - now;
            if (pacingRemaining > TimeSpan.Zero)
            {
                await Task.Delay(
                    pacingRemaining < DatabasePollInterval ? pacingRemaining : DatabasePollInterval,
                    _timeProvider,
                    cancellationToken);
                continue; // Re-read cooldown after the wait; another request may have received a 429.
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var interval = _options.RequestInterval > TimeSpan.Zero
                ? _options.RequestInterval
                : TimeSpan.FromSeconds(1);
            var affected = await db.SpotifyApiGateStates
                .Where(row => row.Id == 1
                    && row.Version == state.Version
                    && row.NotBefore <= now
                    && row.NextRequestAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.NextRequestAt, now + interval)
                    .SetProperty(row => row.UpdatedAt, now)
                    .SetProperty(row => row.Version, row => row.Version + 1), cancellationToken);

            if (affected == 1) return;
            await Task.Delay(DatabasePollInterval, _timeProvider, cancellationToken);
        }
    }

    public async Task<SpotifyCooldownResult> RecordAsync(
        RetryConditionHeaderValue? retryAfter,
        string? spotifyReason,
        CancellationToken cancellationToken)
    {
        var isQuota = string.Equals(spotifyReason, "QUOTA_EXCEEDED", StringComparison.OrdinalIgnoreCase);
        if (_scopeFactory is null)
        {
            var remaining = RecordMemory(retryAfter, isQuota);
            lock (_memoryLock)
                return new SpotifyCooldownResult(_memoryNotBefore, remaining, 0, _memoryQuotaCooldown);
        }

        // Block other callers immediately, including while persistence is in flight.
        var receivedAt = _timeProvider.GetUtcNow();
        RememberDeadline(AddSaturating(receivedAt,
            GetExplicitRetryDelay(retryAfter, receivedAt) ?? MissingHeaderFallback(isQuota)), isQuota);
        try
        {
            for (var attempt = 0; attempt < 32; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = _timeProvider.GetUtcNow();
                var state = await ReadStateAsync(cancellationToken);
                var explicitDelay = GetExplicitRetryDelay(retryAfter, now);
                var lastFailureAt = isQuota ? state.QuotaLastRateLimitAt : state.NormalLastRateLimitAt;
                var oldCount = isQuota ? state.QuotaMissingRetryAfterCount : state.NormalMissingRetryAfterCount;
                if (lastFailureAt is { } previousFailure
                    && now - previousFailure >= NonNegative(_options.BackoffResetQuietPeriod))
                    oldCount = 0;

                var nextCount = explicitDelay is null ? oldCount + 1 : 0;
                var delay = explicitDelay ?? GetFallbackDelay(isQuota, nextCount);
                var candidate = AddSaturating(now, delay);
                var extendsDeadline = candidate > state.NotBefore;
                var notBefore = extendsDeadline ? candidate : state.NotBefore;
                var deadlineIsQuota = extendsDeadline ? isQuota : state.NotBeforeIsQuotaExceeded;

                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var query = db.SpotifyApiGateStates.Where(row => row.Id == 1 && row.Version == state.Version);
                var affected = isQuota
                    ? await query.ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.NotBefore, notBefore)
                        .SetProperty(row => row.NotBeforeIsQuotaExceeded, deadlineIsQuota)
                        .SetProperty(row => row.QuotaMissingRetryAfterCount, nextCount)
                        .SetProperty(row => row.QuotaLastRateLimitAt, now)
                        .SetProperty(row => row.UpdatedAt, now)
                        .SetProperty(row => row.Version, row => row.Version + 1), cancellationToken)
                    : await query.ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => row.NotBefore, notBefore)
                        .SetProperty(row => row.NotBeforeIsQuotaExceeded, deadlineIsQuota)
                        .SetProperty(row => row.NormalMissingRetryAfterCount, nextCount)
                        .SetProperty(row => row.NormalLastRateLimitAt, now)
                        .SetProperty(row => row.UpdatedAt, now)
                        .SetProperty(row => row.Version, row => row.Version + 1), cancellationToken);

                if (affected == 1)
                {
                    RememberDeadline(notBefore, deadlineIsQuota);
                    return new SpotifyCooldownResult(notBefore, notBefore - now, nextCount, deadlineIsQuota);
                }
            }

            throw new InvalidOperationException("Could not persist Spotify's rate limit after concurrent updates.");
        }
        catch (Exception)
        {
            var now = _timeProvider.GetUtcNow();
            var delay = GetExplicitRetryDelay(retryAfter, now) ?? MissingHeaderFallback(isQuota);
            RememberDeadline(AddSaturating(now, delay), isQuota);
            lock (_memoryLock)
                return new SpotifyCooldownResult(_memoryNotBefore, _memoryNotBefore - now, 0,
                    _memoryQuotaCooldown, Persisted: false);
        }
    }

    /// <summary>
    /// A successful Web API call ends the normal missing-header streak once its
    /// cooldown has elapsed. Quota fallback history has its own quiet-period rule.
    /// </summary>
    public async Task RecordSuccessAsync(CancellationToken cancellationToken)
    {
        if (_scopeFactory is null) return;

        for (var attempt = 0; attempt < 16; attempt++)
        {
            var now = _timeProvider.GetUtcNow();
            var state = await ReadStateAsync(cancellationToken);
            var resetNormal = state.NotBefore <= now
                && (state.NormalMissingRetryAfterCount != 0 || state.NormalLastRateLimitAt is not null);
            var resetQuota = state.QuotaLastRateLimitAt is { } quotaAt
                && now - quotaAt >= NonNegative(_options.BackoffResetQuietPeriod)
                && (state.QuotaMissingRetryAfterCount != 0 || state.QuotaLastRateLimitAt is not null);
            if (!resetNormal && !resetQuota) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var query = db.SpotifyApiGateStates.Where(row => row.Id == 1 && row.Version == state.Version);
            var affected = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.NormalMissingRetryAfterCount, resetNormal ? 0 : state.NormalMissingRetryAfterCount)
                .SetProperty(row => row.NormalLastRateLimitAt, resetNormal ? null : state.NormalLastRateLimitAt)
                .SetProperty(row => row.QuotaMissingRetryAfterCount, resetQuota ? 0 : state.QuotaMissingRetryAfterCount)
                .SetProperty(row => row.QuotaLastRateLimitAt, resetQuota ? null : state.QuotaLastRateLimitAt)
                .SetProperty(row => row.UpdatedAt, now)
                .SetProperty(row => row.Version, row => row.Version + 1), cancellationToken);
            if (affected == 1) return;
        }
    }

    public async Task<DateTimeOffset?> GetNotBeforeAsync(CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
        {
            lock (_memoryLock)
                return _memoryNotBefore > _timeProvider.GetUtcNow() ? _memoryNotBefore : null;
        }

        var state = await ReadStateAsync(cancellationToken);
        RememberDeadline(state.NotBefore, state.NotBeforeIsQuotaExceeded);
        lock (_memoryLock)
            return _memoryNotBefore > _timeProvider.GetUtcNow() ? _memoryNotBefore : null;
    }

    private async Task<SpotifyApiGateState> ReadStateAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await db.SpotifyApiGateStates.AsNoTracking().SingleOrDefaultAsync(row => row.Id == 1, cancellationToken);
        if (state is not null) return state;

        var now = _timeProvider.GetUtcNow();
        var legacyCooldown = await db.ServicePlaylistMappings.AsNoTracking()
            .Where(link => link.Service == "spotify"
                && (link.LastSyncStatus == "rate_limited"
                    || link.LastSyncStatus == "quota_limited")
                && link.NextAttemptAt != null)
            .OrderByDescending(link => link.NextAttemptAt)
            .Select(link => new { link.NextAttemptAt, link.LastSyncStatus })
            .FirstOrDefaultAsync(cancellationToken);
        db.SpotifyApiGateStates.Add(new SpotifyApiGateState
        {
            Id = 1,
            NotBefore = legacyCooldown?.NextAttemptAt is { } deadline && deadline > now
                ? deadline
                : DateTimeOffset.MinValue,
            NextRequestAt = DateTimeOffset.MinValue,
            NotBeforeIsQuotaExceeded = legacyCooldown?.LastSyncStatus == "quota_limited",
            UpdatedAt = now,
            Version = 0
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent first request inserted the singleton row.
        }

        await using var reloadScope = _scopeFactory!.CreateAsyncScope();
        var reloadDb = reloadScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await reloadDb.SpotifyApiGateStates.AsNoTracking()
            .SingleAsync(row => row.Id == 1, cancellationToken);
    }

    private TimeSpan RecordMemory(RetryConditionHeaderValue? retryAfter, bool isQuotaExceeded)
    {
        var now = _timeProvider.GetUtcNow();
        var delay = GetExplicitRetryDelay(retryAfter, now) ?? MissingHeaderFallback(isQuotaExceeded);
        var candidate = AddSaturating(now, delay);
        lock (_memoryLock)
        {
            if (candidate > _memoryNotBefore)
            {
                _memoryNotBefore = candidate;
                _memoryQuotaCooldown = isQuotaExceeded;
            }
            return _memoryNotBefore - now;
        }
    }

    private void ThrowIfLocallyActive()
    {
        lock (_memoryLock)
        {
            var remaining = _memoryNotBefore - _timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                throw CreateRateLimitException(remaining, _memoryQuotaCooldown);
        }
    }

    private void RememberDeadline(DateTimeOffset deadline, bool isQuotaExceeded)
    {
        if (deadline <= _timeProvider.GetUtcNow()) return;
        lock (_memoryLock)
        {
            if (deadline > _memoryNotBefore)
            {
                _memoryNotBefore = deadline;
                _memoryQuotaCooldown = isQuotaExceeded;
            }
        }
    }

    private TimeSpan MissingHeaderFallback(bool isQuotaExceeded)
        => isQuotaExceeded
            ? NonNegative(_options.QuotaMissingRetryAfterBaseDelay)
            : NonNegative(_options.MissingRetryAfterBaseDelay);

    private TimeSpan GetFallbackDelay(bool isQuotaExceeded, int count)
    {
        var delay = MissingHeaderFallback(isQuotaExceeded);
        var max = NonNegative(_options.MissingRetryAfterMaxDelay);
        for (var i = 1; i < count && delay < max; i++)
        {
            delay = delay > max - delay ? max : delay + delay;
        }
        return delay > max ? max : delay;
    }

    private static TimeSpan? GetExplicitRetryDelay(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (retryAfter?.Date is { } date && date > now) return date - now;
        return null;
    }

    private static DateTimeOffset AddSaturating(DateTimeOffset now, TimeSpan delay)
        => delay > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delay;

    private static TimeSpan NonNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static PlatformApiException CreateRateLimitException(TimeSpan remaining, bool isQuotaExceeded)
        => new(
            isQuotaExceeded ? "spotify_quota_exceeded" : "spotify_rate_limited",
            isQuotaExceeded
                ? "Spotify's development quota is temporarily exhausted. Try again later."
                : "Spotify is rate limiting requests. Try again later.",
            (int)HttpStatusCode.TooManyRequests,
            remaining);
}
