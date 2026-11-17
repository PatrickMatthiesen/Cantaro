using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistSyncCoordinatorPostgresTests
{
    [PostgresTheory]
    [InlineData("credential")]
    [InlineData("unauthorized")]
    [InlineData("unknown")]
    public async Task NewLinkExport_RealClientRecoversKnownRejectionsWithoutReplayingUnknownOutcomes(string failure)
    {
        await using var scenario = await Scenario.CreateAsync();
        var playlist = scenario.AddPlaylist("Road songs", []);
        var account = await scenario.Db.ConnectedServiceAccounts.SingleAsync(item => item.Id == 7);
        account.EncryptedRefreshToken = "refresh";
        account.Scopes = "https://www.googleapis.com/auth/youtube";
        using var handler = new CreationAuthHandler
        {
            CreationFailure = failure == "unauthorized" ? HttpStatusCode.Unauthorized
                : failure == "unknown" ? HttpStatusCode.ServiceUnavailable : null
        };
        var clients = 0;
        var youtube = new YouTubeService(new ConfigurationBuilder().Build(), scenario.Db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance,
            (connected, _) =>
            {
                // Both writer validations succeed. Credential acquisition for the POST then fails.
                if (++clients == 3 && failure == "credential")
                    throw new TokenResponseException(new TokenErrorResponse { Error = "invalid_grant" });
                var sdk = new Google.Apis.YouTube.v3.YouTubeService(new Google.Apis.Services.BaseClientService.Initializer
                {
                    HttpClientFactory = handler, ApplicationName = "Cantaro.Test",
                    DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None
                });
                sdk.HttpClient.MessageHandler.AddUnsuccessfulResponseHandler(new YouTubeService.AccessRejectionHandler(
                    ct => ProviderReconnectState.MarkAsync(scenario.Db, connected, "access_token_rejected", ct)));
                return Task.FromResult(sdk);
            });
        var writer = new YouTubePlaylistWriter(youtube);
        var coordinator = new PlaylistSyncCoordinator(scenario.Db, [scenario.Provider("youtube")], [writer],
            TimeProvider.System, NullLogger<PlaylistSyncCoordinator>.Instance);
        scenario.Db.MusicSyncJobs.Add(new MusicSyncJob
        {
            Id = Guid.NewGuid(), UserId = UserId, ConnectedServiceAccountId = 7, Service = "youtube",
            Status = MusicSyncJobStatuses.Queued, PlaylistCount = 1,
            PlaylistsJson = JsonSerializer.Serialize(new[] { new MusicSyncJobPlaylist("", playlist.Name, 0, playlist.Id, "youtube-owner") }),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await scenario.SaveAsync();
        var processor = new MusicSyncJobProcessor(scenario.Db,
            new PlatformRegistry([new MusicSyncJobProcessorTests.StubPlatformService("youtube")]), new MusicSyncThrottleService(),
            NullLogger<MusicSyncJobProcessor>.Instance, new OutboundPlaylistSyncService(scenario.Db, [writer], coordinator));

        Assert.True(await processor.ProcessNextAsync(default));
        var paused = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        if (failure == "unknown")
        {
            Assert.Equal(MusicSyncJobStatuses.Failed, paused.Status);
            Assert.Equal("creation_uncertain", (await scenario.Db.ServicePlaylistMappings.SingleAsync()).State);
            Assert.Equal(1, handler.Posts);
            Assert.False(await processor.ProcessNextAsync(default));
            Assert.Equal(1, handler.Posts);
            return;
        }
        Assert.Equal(MusicSyncJobStatuses.Queued, paused.Status);
        Assert.Equal(0, paused.ProcessedPlaylistCount);
        Assert.Null(paused.ResultsJson);
        Assert.Equal(3, clients);
        Assert.Equal(failure == "credential" ? 0 : 1, handler.Posts);
        Assert.Empty(await scenario.Db.ServicePlaylistMappings.ToListAsync());
        Assert.False(await processor.ProcessNextAsync(default));
        await scenario.Db.ConnectedServiceAccounts.Where(item => item.Id == 7).ExecuteUpdateAsync(set => set
            .SetProperty(item => item.ConnectionState, "connected").SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1));

        Assert.True(await processor.ProcessNextAsync(default));
        var resumed = await scenario.Db.MusicSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(MusicSyncJobStatuses.Completed, resumed.Status);
        Assert.Equal(1, resumed.ProcessedPlaylistCount);
        Assert.Equal(failure == "credential" ? 1 : 2, handler.Posts);
        Assert.Equal("youtube-created", (await scenario.Db.ServicePlaylistMappings.SingleAsync()).ServicePlaylistId);
    }

    private sealed class CreationAuthHandler : HttpMessageHandler, Google.Apis.Http.IHttpClientFactory
    {
        public int Posts { get; private set; }
        public HttpStatusCode? CreationFailure { get; init; }
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
            => new(new ConfigurableMessageHandler(this));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (request.Method == HttpMethod.Post)
            {
                Assert.EndsWith("/playlists", path);
                Posts++;
                if (Posts == 1 && CreationFailure is { } failure)
                    return Task.FromResult(new HttpResponseMessage(failure)
                    { Content = new StringContent(JsonSerializer.Serialize(new { error = new { code = (int)failure, message = "Rejected" } }),
                        Encoding.UTF8, "application/json") });
                body = "{\"id\":\"youtube-created\",\"status\":{\"privacyStatus\":\"private\"}}";
            }
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                body = path.EndsWith("/channels", StringComparison.Ordinal) ? "{\"items\":[{\"id\":\"youtube-owner\"}]}"
                    : path.EndsWith("/playlists", StringComparison.Ordinal) ? "{\"items\":[{\"id\":\"youtube-created\"}]}"
                    : path.EndsWith("/playlistItems", StringComparison.Ordinal) ? "{\"items\":[]}"
                    : throw new InvalidOperationException($"Unexpected YouTube request {path}");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
