using System.Net;

namespace Cantaro.Api.Services;

public sealed class MyAnimeListRequestGate
{
    internal static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MissingHeaderCooldown = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumInterval;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextRequestAt = DateTimeOffset.MinValue;

    public MyAnimeListRequestGate(TimeProvider timeProvider)
        : this(timeProvider, DefaultMinimumInterval)
    {
    }

    internal MyAnimeListRequestGate(TimeProvider timeProvider, TimeSpan minimumInterval)
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
                        return new Lease(_requestLock);
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
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAt = response.Headers.RetryAfter?.Delta is { } delta
                    ? now + delta
                    : response.Headers.RetryAfter?.Date ?? now + MissingHeaderCooldown;
                if (retryAt > _cooldownUntil)
                {
                    _cooldownUntil = retryAt;
                }
            }

            var remaining = _cooldownUntil - now;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    private sealed class Lease(SemaphoreSlim requestLock) : IDisposable
    {
        private SemaphoreSlim? _requestLock = requestLock;

        public void Dispose() => Interlocked.Exchange(ref _requestLock, null)?.Release();
    }
}
