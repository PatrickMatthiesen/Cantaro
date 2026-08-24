using System.Collections.Concurrent;

namespace Cantaro.Api.Services;

public sealed class MyAnimeListTokenRefreshGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _accountLocks = new();

    public async Task<IDisposable> AcquireAsync(int accountId, CancellationToken cancellationToken)
    {
        var accountLock = _accountLocks.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await accountLock.WaitAsync(cancellationToken);
        return new Lease(accountLock);
    }

    private sealed class Lease(SemaphoreSlim accountLock) : IDisposable
    {
        private SemaphoreSlim? _accountLock = accountLock;

        public void Dispose() => Interlocked.Exchange(ref _accountLock, null)?.Release();
    }
}
