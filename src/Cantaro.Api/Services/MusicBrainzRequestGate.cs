using System.Net.Http.Headers;

namespace Cantaro.Api.Services;

public sealed class MusicBrainzRequestGate(TimeProvider timeProvider)
{
    private static readonly TimeSpan InitialFallbackDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumFallbackDelay = TimeSpan.FromMinutes(5);
    private readonly Lock _lock = new();
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;
    private string? _cooldownSource;
    private int _consecutiveUnavailableResponses;
    private long _cooldownRevision;

    public long CooldownRevision
    {
        get
        {
            lock (_lock) return _cooldownRevision;
        }
    }

    public bool IsCoolingDown
    {
        get
        {
            lock (_lock) return _notBefore > timeProvider.GetUtcNow();
        }
    }

    public DateTimeOffset NotBefore
    {
        get
        {
            lock (_lock) return _notBefore;
        }
    }

    public string? CooldownSource
    {
        get
        {
            lock (_lock) return _notBefore > timeProvider.GetUtcNow() ? _cooldownSource : null;
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            lock (_lock) delay = _notBefore - timeProvider.GetUtcNow();
            if (delay <= TimeSpan.Zero) return;
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    public TimeSpan Defer(RetryConditionHeaderValue? retryAfter)
    {
        lock (_lock)
        {
            _consecutiveUnavailableResponses++;
            _cooldownRevision++;
            var now = timeProvider.GetUtcNow();
            var delay = ReadRetryAfter(retryAfter, now) ?? GetFallbackDelay(_consecutiveUnavailableResponses);
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            var requestedNotBefore = now + delay;
            if (requestedNotBefore > _notBefore)
            {
                _notBefore = requestedNotBefore;
                _cooldownSource = retryAfter == null ? "fallback" : "retry-after";
            }
            return _notBefore - now;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveUnavailableResponses = 0;
            _notBefore = DateTimeOffset.MinValue;
            _cooldownSource = null;
        }
    }

    private static TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter?.Delta is { } delta) return delta;
        if (retryAfter?.Date is { } date) return date - now;
        return null;
    }

    private static TimeSpan GetFallbackDelay(int failureCount)
    {
        var exponent = Math.Min(failureCount - 1, 4);
        var delay = TimeSpan.FromTicks(InitialFallbackDelay.Ticks * (1L << exponent));
        return delay <= MaximumFallbackDelay ? delay : MaximumFallbackDelay;
    }
}
