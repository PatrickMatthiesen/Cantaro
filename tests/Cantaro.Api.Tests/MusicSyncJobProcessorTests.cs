using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MusicSyncJobProcessorTests
{
    [Fact]
    public async Task ProcessNextAsync_PersistsProgressAndCompletion()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        var playlists = new[]
        {
            new MusicSyncJobPlaylist("one", "First", 3),
            new MusicSyncJobPlaylist("two", "Second", 5)
        };
        var job = new MusicSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = 42,
            Service = "test",
            PlaylistsJson = JsonSerializer.Serialize(playlists),
            Status = MusicSyncJobStatuses.Queued,
            PlaylistCount = playlists.Length,
            SongCount = 8,
            EstimatedNewSongCount = 8,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.MusicSyncJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var platform = new StubPlatformService();
        var throttle = new MusicSyncThrottleService();
        var processor = new MusicSyncJobProcessor(
            dbContext,
            new PlatformRegistry([platform]),
            throttle,
            NullLogger<MusicSyncJobProcessor>.Instance);

        Assert.True(await processor.ProcessNextAsync(CancellationToken.None));

        var persisted = await dbContext.MusicSyncJobs.SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, persisted.Status);
        Assert.Equal(2, persisted.ProcessedPlaylistCount);
        Assert.Equal(8, persisted.ProcessedSongCount);
        Assert.Equal(2, persisted.SuccessCount);
        Assert.Equal(0, persisted.FailureCount);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Equal(["one", "two"], platform.SyncedPlaylistIds);
        Assert.Equal(8, throttle.GetStatus(42, "test", DateTimeOffset.UtcNow).Usage);
    }

    [Fact]
    public void ThrottleService_ExpiresOldUsageAndReportsRemainingCapacity()
    {
        var throttle = new MusicSyncThrottleService();
        var now = DateTimeOffset.UtcNow;

        throttle.AddUsage(7, "youtube", 125, now);

        var current = throttle.GetStatus(7, "youtube", now);
        Assert.Equal(125, current.Usage);
        Assert.Equal(MusicSyncThrottleService.SongSyncLimitPerWindow - 125, current.Remaining);
        Assert.Equal(0, throttle.GetStatus(7, "youtube", now.AddMinutes(11)).Usage);
    }

    private sealed class StubPlatformService : IPlatformService
    {
        public string PlatformId => "test";
        public List<string> SyncedPlaylistIds { get; } = [];

        public Task<Guid> SyncPlaylistAsync(int userId, string playlistId, CancellationToken cancellationToken)
        {
            SyncedPlaylistIds.Add(playlistId);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId) => throw new NotSupportedException();
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotSupportedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode, string redirectUri) => throw new NotSupportedException();
        public Task DisconnectAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId) => throw new NotSupportedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = null;
            return true;
        }
    }
}
