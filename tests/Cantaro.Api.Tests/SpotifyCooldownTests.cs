using System.Net.Http.Headers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SpotifyCooldownTests
{
    [Fact]
    public async Task RecordAsync_PersistsExplicitCooldownAcrossInstances_AndAcquireFailsFast()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var first = fixture.CreateGate();
        var expectedDeadline = DateTimeOffset.UtcNow.AddHours(3);

        var result = await first.RecordAsync(
            new RetryConditionHeaderValue(expectedDeadline),
            "RATE_LIMITED",
            CancellationToken.None);

        Assert.True(Math.Abs((result.NotBefore - expectedDeadline).TotalSeconds) < 1);
        Assert.InRange(result.RetryAfter, TimeSpan.FromHours(3) - TimeSpan.FromSeconds(1), TimeSpan.FromHours(3));

        var second = fixture.CreateGate();
        Assert.Equal(result.NotBefore, await second.GetNotBeforeAsync(CancellationToken.None));
        var exception = await Assert.ThrowsAsync<Cantaro.Api.Services.PlatformApiException>(
            () => second.AcquireAsync(CancellationToken.None));
        Assert.Equal("spotify_rate_limited", exception.Code);
        Assert.Equal(429, exception.StatusCode);
        Assert.True(exception.RetryAfter > TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task RecordAsync_StorageFailureStillBlocksThisInstanceForKnownRetryAfter()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var gate = new SpotifyCooldown(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new SpotifyRequestGateOptions()));

        var result = await gate.RecordAsync(
            new RetryConditionHeaderValue(TimeSpan.FromHours(3)),
            "RATE_LIMITED",
            CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.True(result.RetryAfter > TimeSpan.FromHours(2));
        var exception = await Assert.ThrowsAsync<Cantaro.Api.Services.PlatformApiException>(
            () => gate.AcquireAsync(CancellationToken.None));
        Assert.Equal("spotify_rate_limited", exception.Code);
        Assert.True(exception.RetryAfter > TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task RecordAsync_UsesIncreasingFallbackForMissingZeroAndPastRetryAfter()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var gate = fixture.CreateGate();

        var missing = await gate.RecordAsync(null, "RATE_LIMITED", CancellationToken.None);
        var zero = await gate.RecordAsync(new RetryConditionHeaderValue(TimeSpan.Zero), "RATE_LIMITED", CancellationToken.None);
        var past = await gate.RecordAsync(
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-2)),
            "RATE_LIMITED",
            CancellationToken.None);

        Assert.InRange(missing.RetryAfter, TimeSpan.FromSeconds(30) - TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        Assert.Equal(1, missing.FallbackCount);
        Assert.Equal(2, zero.FallbackCount);
        Assert.InRange(zero.RetryAfter, TimeSpan.FromMinutes(1) - TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        Assert.Equal(3, past.FallbackCount);
        Assert.InRange(past.RetryAfter, TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task RecordAsync_UsesSeparateQuotaFallbackAndNeverShortensExistingDeadline()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var gate = fixture.CreateGate();

        var ordinary = await gate.RecordAsync(
            new RetryConditionHeaderValue(TimeSpan.FromHours(2)), "RATE_LIMITED", CancellationToken.None);
        var quota = await gate.RecordAsync(null, "QUOTA_EXCEEDED", CancellationToken.None);

        Assert.Equal(1, quota.FallbackCount);
        Assert.False(quota.IsQuotaExceeded); // The ordinary two-hour deadline remains the active one.
        Assert.Equal(ordinary.NotBefore, quota.NotBefore);
        Assert.InRange(quota.RetryAfter, TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1), TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task RecordAsync_CapsFallbackAtOneHourButHonorsLongerExplicitRetryAfter()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        var gate = fixture.CreateGate(new SpotifyRequestGateOptions(), clock);
        SpotifyCooldownResult? result = null;

        for (var i = 0; i < 9; i++)
            result = await gate.RecordAsync(null, "RATE_LIMITED", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(9, result.FallbackCount);
        Assert.Equal(TimeSpan.FromHours(1), result.RetryAfter);

        var explicitDelay = await gate.RecordAsync(
            new RetryConditionHeaderValue(TimeSpan.FromHours(3)),
            "RATE_LIMITED",
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(3), explicitDelay.RetryAfter);
    }

    [Fact]
    public async Task RecordSuccess_ResetsNormalFallbackAfterCooldownButPreservesQuotaUntilQuietPeriod()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        var gate = fixture.CreateGate(new SpotifyRequestGateOptions(), clock);
        await gate.RecordAsync(null, "RATE_LIMITED", CancellationToken.None);
        await gate.RecordAsync(null, "QUOTA_EXCEEDED", CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(6));

        await gate.RecordSuccessAsync(CancellationToken.None);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var state = await db.SpotifyApiGateStates.SingleAsync();
            Assert.Equal(0, state.NormalMissingRetryAfterCount);
            Assert.Null(state.NormalLastRateLimitAt);
            Assert.Equal(1, state.QuotaMissingRetryAfterCount);
            Assert.NotNull(state.QuotaLastRateLimitAt);
        }

        var nextNormal = await gate.RecordAsync(null, "RATE_LIMITED", CancellationToken.None);
        var nextQuota = await gate.RecordAsync(null, "QUOTA_EXCEEDED", CancellationToken.None);
        Assert.Equal(1, nextNormal.FallbackCount);
        Assert.Equal(2, nextQuota.FallbackCount);

        clock.Advance(TimeSpan.FromHours(24));
        var afterQuietPeriod = await gate.RecordAsync(null, "QUOTA_EXCEEDED", CancellationToken.None);
        Assert.Equal(1, afterQuietPeriod.FallbackCount);
    }

    [Fact]
    public async Task RecordSuccess_DoesNotClearAnActiveCooldownOrQuotaFallbackHistory()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var gate = fixture.CreateGate();
        await gate.RecordAsync(null, "QUOTA_EXCEEDED", CancellationToken.None);

        await gate.RecordSuccessAsync(CancellationToken.None);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var state = await db.SpotifyApiGateStates.SingleAsync();
        Assert.Equal(1, state.QuotaMissingRetryAfterCount);
        Assert.True(state.NotBefore > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AcquireAsync_PacesConcurrentInstancesUsingTheSharedDatabase()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var options = new SpotifyRequestGateOptions { RequestInterval = TimeSpan.FromMilliseconds(50) };
        var first = fixture.CreateGate(options);
        var second = fixture.CreateGate(options);
        var acquiredAt = new DateTimeOffset[2];

        await Task.WhenAll(
            Task.Run(async () => { await first.AcquireAsync(CancellationToken.None); acquiredAt[0] = DateTimeOffset.UtcNow; }),
            Task.Run(async () => { await second.AcquireAsync(CancellationToken.None); acquiredAt[1] = DateTimeOffset.UtcNow; }));

        Assert.True(Math.Abs((acquiredAt[0] - acquiredAt[1]).TotalMilliseconds) >= 35);
    }

    [Fact]
    public async Task AcquireAsync_RechecksCooldownAfterWaitingForPacingSlot()
    {
        await using var fixture = await GateFixture.CreateAsync();
        var options = new SpotifyRequestGateOptions { RequestInterval = TimeSpan.FromSeconds(1) };
        var first = fixture.CreateGate(options);
        var queued = fixture.CreateGate(options);
        var rateLimited = fixture.CreateGate(options);
        await first.AcquireAsync(CancellationToken.None);

        var queuedRequest = queued.AcquireAsync(CancellationToken.None);
        await Task.Delay(50);
        await rateLimited.RecordAsync(
            new RetryConditionHeaderValue(TimeSpan.FromMinutes(10)),
            "RATE_LIMITED",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<Cantaro.Api.Services.PlatformApiException>(() => queuedRequest);
        Assert.Equal("spotify_rate_limited", exception.Code);
        Assert.True(exception.RetryAfter > TimeSpan.FromMinutes(9));
    }

    private sealed class GateFixture(string databasePath, ServiceProvider services) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public SpotifyCooldown CreateGate(
            SpotifyRequestGateOptions? options = null,
            TimeProvider? timeProvider = null)
            => new(
                Services.GetRequiredService<IServiceScopeFactory>(),
                timeProvider ?? TimeProvider.System,
                Options.Create(options ?? new SpotifyRequestGateOptions()));

        public static async Task<GateFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"cantaro-spotify-gate-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.EnsureCreatedAsync();
            }
            return new GateFixture(databasePath, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
            if (File.Exists(databasePath + "-shm")) File.Delete(databasePath + "-shm");
            if (File.Exists(databasePath + "-wal")) File.Delete(databasePath + "-wal");
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime;

        public override DateTimeOffset GetUtcNow() => _currentTime;

        public void Advance(TimeSpan amount) => _currentTime += amount;
    }
}
