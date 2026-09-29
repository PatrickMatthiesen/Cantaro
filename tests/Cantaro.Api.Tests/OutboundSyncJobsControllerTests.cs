using System.Security.Claims;
using System.Text.Json;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class OutboundSyncJobsControllerTests
{
    [Fact]
    public async Task CreateOutbound_RejectsAnotherUsersPlaylistBeforeProviderValidation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var foreign = fixture.AddPlaylist(userId: 42);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.CreateOutbound(
            new OutboundSyncRequest("youtube", foreign.Id), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(0, fixture.Writer.ValidateCount);
        Assert.Empty(fixture.Db.MusicSyncJobs);
    }

    [Fact]
    public async Task CreateOutbound_RejectsActivePlatformJobBeforeProviderValidation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.AddPlaylist();
        fixture.Db.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = fixture.UserId, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.CreateOutbound(
            new OutboundSyncRequest("youtube", source.Id), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(0, fixture.Writer.ValidateCount);
        Assert.Equal(0, fixture.Writer.WriteCount);
    }

    [Fact]
    public async Task CreateOutbound_ReturnsProviderScopeFailureWithoutQueuing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.AddPlaylist();
        fixture.Writer.ValidationError = new PlatformApiException(
            "destination_unavailable", "Reconnect YouTube to allow playlist edits.", 409);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.CreateOutbound(
            new OutboundSyncRequest("youtube", source.Id), CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Empty(fixture.Db.MusicSyncJobs);
        Assert.Equal(0, fixture.Writer.WriteCount);
    }

    [Fact]
    public async Task CreateOutbound_QueuesPinnedSourceForDestinationCreationWithoutWritingProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.AddPlaylist();
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.CreateOutbound(
            new OutboundSyncRequest("youtube", source.Id), CancellationToken.None);

        var response = Assert.IsType<MusicSyncJobResponse>(
            Assert.IsType<AcceptedAtActionResult>(result.Result).Value);
        var job = Assert.Single(fixture.Db.MusicSyncJobs);
        var selected = Assert.Single(JsonSerializer.Deserialize<List<MusicSyncJobPlaylist>>(job.PlaylistsJson)!);
        Assert.Equal("export", response.Direction);
        Assert.Equal(source.Id.ToString(), response.CantaroPlaylistId);
        Assert.Equal(fixture.AccountId, job.ConnectedServiceAccountId);
        Assert.Equal(source.Id, selected.CantaroPlaylistId);
        Assert.Equal(string.Empty, selected.Id);
        Assert.Equal("channel", fixture.Writer.LastValidatedAccount?.ExpectedExternalAccountId);
        Assert.Equal(MusicSyncJobStatuses.Queued, job.Status);
        Assert.Equal(1, fixture.Writer.ValidateCount);
        Assert.Equal(0, fixture.Writer.WriteCount);
        Assert.Empty(fixture.Db.ServicePlaylistMappings);
    }

    [Fact]
    public async Task CreateOutbound_RejectsUnlinkedLegacyDestinationWithoutQueuing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.AddPlaylist();
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.CreateOutbound(
            new OutboundSyncRequest("youtube", source.Id, "unrelated-playlist"), CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, response.StatusCode);
        Assert.Empty(fixture.Db.MusicSyncJobs);
        Assert.Equal(0, fixture.Writer.ValidateCount);
        Assert.Equal(0, fixture.Writer.WriteCount);
    }

    [Fact]
    public async Task CreateOutbound_QueuesExistingLinkedOutboundMapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = fixture.AddPlaylist();
        fixture.Db.ServicePlaylistMappings.Add(new ServicePlaylistMapping
        {
            Id = Guid.NewGuid(), PlaylistId = source.Id, ConnectedServiceAccountId = fixture.AccountId,
            Service = "youtube", ServicePlaylistId = "linked-playlist", SyncMode = "from_cantaro"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.CreateOutbound(
            new OutboundSyncRequest("youtube", source.Id), CancellationToken.None);

        Assert.IsType<AcceptedAtActionResult>(result.Result);
        var job = Assert.Single(fixture.Db.MusicSyncJobs);
        var selected = Assert.Single(JsonSerializer.Deserialize<List<MusicSyncJobPlaylist>>(job.PlaylistsJson)!);
        Assert.Equal("linked-playlist", selected.Id);
        Assert.Equal(1, fixture.Writer.ValidateCount);
        Assert.Equal(0, fixture.Writer.WriteCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly UserManager<User> _userManager;

        private Fixture(ApplicationDbContext db, ServiceProvider services, UserManager<User> userManager,
            FakePlatform platform, FakeWriter writer)
        {
            Db = db;
            _services = services;
            _userManager = userManager;
            Writer = writer;
            Controller = new SyncJobsController(db, userManager, new PlatformRegistry([platform]),
                new MusicSyncThrottleService(), services.GetRequiredService<IServiceScopeFactory>(),
                new OutboundPlaylistSyncService(db, [writer]))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test"))
                    }
                }
            };
        }

        public int UserId => 901;
        public int AccountId => 307;
        public ApplicationDbContext Db { get; }
        public SyncJobsController Controller { get; }
        public FakeWriter Writer { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"outbound-job-{Guid.NewGuid()}").Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var userManager = CreateUserManager(db);
            var user = TestUserFactory.Create(901, "outbound-jobs@example.com");
            var account = new ConnectedServiceAccount
            {
                Id = 307, UserId = user.Id, Service = "youtube", ExternalAccountId = "channel",
                ConnectionState = "connected"
            };
            db.Users.Add(user);
            db.ConnectedServiceAccounts.Add(account);
            await db.SaveChangesAsync();
            var services = new ServiceCollection().BuildServiceProvider();
            return new Fixture(db, services, userManager, new FakePlatform(account), new FakeWriter());
        }

        public Playlist AddPlaylist(int? userId = null)
        {
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(), UserId = userId ?? UserId, Name = "Road songs",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            };
            Db.Playlists.Add(playlist);
            return playlist;
        }

        public async ValueTask DisposeAsync()
        {
            _userManager.Dispose();
            _services.Dispose();
            await Db.DisposeAsync();
        }

        private static UserManager<User> CreateUserManager(ApplicationDbContext db)
        {
            var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
            return new UserManager<User>(store, Options.Create(new IdentityOptions()),
                new PasswordHasher<User>(), [], [], new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
        }
    }

    private sealed class FakeWriter : IPlaylistWriter
    {
        public string PlatformId => "youtube";
        public PlatformApiException? ValidationError { get; set; }
        public int ValidateCount { get; private set; }
        public int WriteCount { get; private set; }
        public PlatformAccountContext? LastValidatedAccount { get; private set; }

        public Task ValidateCreationAsync(PlatformAccountContext account, CancellationToken cancellationToken)
        {
            ValidateCount++;
            LastValidatedAccount = account;
            if (ValidationError is { } error) throw error;
            return Task.CompletedTask;
        }

        public Task<string> CreatePlaylistAsync(
            PlatformAccountContext account, string name, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult("created-playlist");
        }

        public Task ValidateDestinationAsync(
            PlatformAccountContext account, string playlistId, CancellationToken cancellationToken)
        {
            ValidateCount++;
            LastValidatedAccount = account;
            if (ValidationError is { } error) throw error;
            return Task.CompletedTask;
        }

        public Task ReconcileAsync(PlatformAccountContext account, string playlistId,
            IReadOnlyList<string> trackIds, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlatform(ConnectedServiceAccount account) : IPlatformService
    {
        public string PlatformId => "youtube";
        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(int userId)
            => Task.FromResult<ConnectedServiceAccount?>(userId == account.UserId ? account : null);
        public string GetAuthorizationUrl(string redirectUri, string state) => throw new NotImplementedException();
        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(int userId, string authorizationCode,
            string redirectUri) => throw new NotImplementedException();
        public Task DisconnectAsync(int userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<PlatformPlaylistDto>> GetPlaylistsAsync(int userId)
            => Task.FromResult<IReadOnlyList<PlatformPlaylistDto>>([]);
        public Task<IReadOnlyList<PlatformSongDto>> GetPlaylistSongsAsync(int userId, string playlistId)
            => throw new NotImplementedException();
        public Task<Guid> SyncPlaylistAsync(PlatformAccountContext context, string playlistId,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public bool TryValidatePlaylistId(string playlistId, out string? error)
        {
            error = string.IsNullOrWhiteSpace(playlistId) ? "Playlist ID required." : null;
            return error is null;
        }
    }
}
