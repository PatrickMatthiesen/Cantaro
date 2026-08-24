using System.Collections.Concurrent;

namespace Cantaro.Api.Services;

public sealed class MediaProviderInitialSyncGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _userLocks = new();

    public async Task<IDisposable> AcquireAsync(int userId, CancellationToken cancellationToken)
    {
        var userLock = _userLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        return new Lease(userLock);
    }

    private sealed class Lease(SemaphoreSlim userLock) : IDisposable
    {
        private SemaphoreSlim? _userLock = userLock;

        public void Dispose() => Interlocked.Exchange(ref _userLock, null)?.Release();
    }
}
