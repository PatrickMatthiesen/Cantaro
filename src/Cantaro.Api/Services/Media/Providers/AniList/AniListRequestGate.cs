using System.Globalization;
using System.Net;

namespace Cantaro.Api.Services;

public sealed class AniListRequestGate
{
    // AniList currently documents a degraded limit of 30 requests/minute. The
    // response limit header can raise or lower this interval at runtime.
    internal static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromSeconds(2.1);
    private static readonly TimeSpan IntervalSafetyMargin = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MissingHeaderCooldown = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;
    private TimeSpan _minimumInterval;

    public AniListRequestGate(TimeProvider timeProvider)
        : this(timeProvider, DefaultMinimumInterval)
    {
    }

    internal AniListRequestGate(TimeProvider timeProvider, TimeSpan minimumInterval)
    {
        _timeProvider = timeProvider;
        _minimumInterval = minimumInterval;
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                TimeSpan delay;
                lock (_stateLock)
                {
                    var now = _timeProvider.GetUtcNow();
                    var allowedAt = _cooldownUntil > _nextRequestAt ? _cooldownUntil : _nextRequestAt;
                    delay = allowedAt - now;
                    if (delay <= TimeSpan.Zero)
                    {
                        _nextRequestAt = now + _minimumInterval;
                        return new RequestLease(_requestLock);
                    }
                }

                await Task.Delay(delay, _timeProvider, cancellationToken);
            }
        }
        catch
        {
            _requestLock.Release();
            throw;
        }
    }

    public TimeSpan? ObserveResponse(HttpResponseMessage response)
    {
        lock (_stateLock)
        {
            var now = _timeProvider.GetUtcNow();
            if (ReadInt64Header(response, "X-RateLimit-Limit") is > 0 and var limit)
            {
                _minimumInterval = TimeSpan.FromSeconds(60d / limit) + IntervalSafetyMargin;
            }

            var resetAt = ReadResetAt(response);
            DateTimeOffset? requestedCooldown = null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAt = ReadRetryAt(response, now);
                requestedCooldown = LaterOf(retryAt, resetAt) ?? now + MissingHeaderCooldown;
            }
            else if (ReadInt64Header(response, "X-RateLimit-Remaining") == 0)
            {
                requestedCooldown = resetAt;
            }

            if (requestedCooldown > _cooldownUntil)
            {
                _cooldownUntil = requestedCooldown.Value;
            }

            var remaining = _cooldownUntil - now;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    private static DateTimeOffset? ReadRetryAt(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return now + delta;
        return response.Headers.RetryAfter?.Date;
    }

    private static DateTimeOffset? ReadResetAt(HttpResponseMessage response)
    {
        var resetSeconds = ReadInt64Header(response, "X-RateLimit-Reset");
        if (resetSeconds is null) return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(resetSeconds.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static long? ReadInt64Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        var value = values.FirstOrDefault();
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? LaterOf(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return first > second ? first : second;
    }

    private sealed class RequestLease(SemaphoreSlim requestLock) : IDisposable
    {
        private SemaphoreSlim? _requestLock = requestLock;

        public void Dispose() => Interlocked.Exchange(ref _requestLock, null)?.Release();
    }
}
