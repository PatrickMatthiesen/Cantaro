using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ProviderReconnectTests
{
    [PostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionalMetadata_DoesNotHideCancellationOrUnrelatedPlatformErrors(bool cancelled)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var account = await AddAccountAsync(schema.Db);
        Exception expected = cancelled ? new OperationCanceledException()
            : new PlatformApiException("platform_account_changed", "The account changed.", 409);
        var service = Service(schema.Db, (_, _) => throw expected);
        Assert.Same(expected, await Record.ExceptionAsync(() => service.GetVideoMetadataAsync(701, "video", default)));
        await schema.Db.Entry(account).ReloadAsync();
        Assert.Equal("connected", account.ConnectionState);
    }

    [PostgresTheory]
    [InlineData("invalid_grant", true)]
    [InlineData("temporarily_unavailable", false)]
    [InlineData("invalid_client", false)]
    [InlineData("cancel", false)]
    public async Task YouTubeRefresh_ClassifiesAndPersistsOnlyPermanentRejection(string error, bool reconnect)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var account = await AddAccountAsync(db);
        var calls = 0;
        var service = Service(db, (_, _) =>
        {
            calls++;
            if (error == "cancel") throw new OperationCanceledException();
            throw new TokenResponseException(new TokenErrorResponse { Error = error });
        });
        var failure = await Record.ExceptionAsync(() => service.CreateYouTubeServiceAsync(account));
        Assert.NotNull(failure);
        await db.Entry(account).ReloadAsync();
        Assert.Equal(reconnect ? "reconnect_required" : "connected", account.ConnectionState);
        Assert.Equal(reconnect, failure is PlatformReconnectRequiredException);
        Assert.Equal("refresh", account.EncryptedRefreshToken);
        if (reconnect)
        {
            await Assert.ThrowsAsync<PlatformReconnectRequiredException>(() => service.CreateYouTubeServiceAsync(account));
            Assert.Equal(1, calls);
        }
    }

    [PostgresFact]
    public async Task YouTubeOldFailure_DoesNotOverwriteNewOAuthCredentials()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var db = schema.Db;
        var account = await AddAccountAsync(db);
        var calls = 0;
        var service = Service(db, async (_, ct) =>
        {
            if (++calls == 1)
            {
                await using var oauth = PlaylistReadIngestion.CreateContext(db);
                await oauth.ConnectedServiceAccounts.Where(item => item.Id == account.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.TokenVersion, 2)
                        .SetProperty(item => item.EncryptedRefreshToken, "new-refresh"), ct);
                throw new TokenResponseException(new TokenErrorResponse { Error = "invalid_grant" });
            }
            return new Google.Apis.YouTube.v3.YouTubeService();
        });
        using var client = await service.CreateYouTubeServiceAsync(account);
        await db.Entry(account).ReloadAsync();
        Assert.Equal("connected", account.ConnectionState);
        Assert.Equal("new-refresh", account.EncryptedRefreshToken);
        Assert.Equal(2, calls);
        Assert.Null(account.ReconnectRequiredAt);
    }

    [PostgresFact]
    public async Task AccountBindingAndVersion_FenceReconnectWrites()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var account = await AddAccountAsync(schema.Db);
        await using var oauth = PlaylistReadIngestion.CreateContext(schema.Db);
        await oauth.ConnectedServiceAccounts.Where(item => item.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ExternalAccountId, "different-account")
                .SetProperty(item => item.TokenVersion, 2));
        Assert.False(await ProviderReconnectState.MarkAsync(schema.Db, account, "refresh_token_invalid", default));
        await schema.Db.Entry(account).ReloadAsync();
        Assert.Equal("connected", account.ConnectionState);
        Assert.Equal("different-account", account.ExternalAccountId);
    }

    [PostgresTheory]
    [InlineData(401, true)]
    [InlineData(403, false)]
    [InlineData(429, false)]
    [InlineData(503, false)]
    public async Task YouTubeFreshAccess_OnlyUnauthorizedRequiresReconnect(int status, bool reconnect)
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var account = await AddAccountAsync(schema.Db);
        var handler = new YouTubeService.AccessRejectionHandler(
            ct => ProviderReconnectState.MarkAsync(schema.Db, account, "access_token_rejected", ct));
        using var response = new HttpResponseMessage((System.Net.HttpStatusCode)status);
        var args = new Google.Apis.Http.HandleUnsuccessfulResponseArgs { Response = response, CancellationToken = default };
        var failure = await Record.ExceptionAsync(() => handler.HandleResponseAsync(args));
        Assert.Equal(reconnect, failure is PlatformReconnectRequiredException);
        if (reconnect) Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            Assert.IsType<HttpRequestException>(failure!.InnerException).StatusCode);
        await schema.Db.Entry(account).ReloadAsync();
        Assert.Equal(reconnect ? "reconnect_required" : "connected", account.ConnectionState);
    }

    [PostgresFact]
    public async Task MissingCredentialReload_RejectsReplacementChannelBeforeCreatingClient()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        var account = await AddAccountAsync(schema.Db);
        account.EncryptedRefreshToken = null;
        await schema.Db.SaveChangesAsync();
        await using var replacement = PlaylistReadIngestion.CreateContext(schema.Db);
        await replacement.ConnectedServiceAccounts.Where(item => item.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ExternalAccountId, "other-channel")
                .SetProperty(item => item.EncryptedRefreshToken, "new-refresh")
                .SetProperty(item => item.TokenVersion, item => item.TokenVersion + 1));
        var service = new YouTubeService(new ConfigurationBuilder().Build(), schema.Db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance);
        var failure = await Assert.ThrowsAsync<PlatformApiException>(() => service.CreateYouTubeServiceAsync(account));
        Assert.Equal("platform_account_changed", failure.Code);
        Assert.Equal("other-channel", account.ExternalAccountId);
        Assert.Equal("connected", account.ConnectionState);
    }

    private static YouTubeService Service(ApplicationDbContext db,
        Func<ConnectedServiceAccount, CancellationToken, Task<Google.Apis.YouTube.v3.YouTubeService>> factory)
        => new(new ConfigurationBuilder().Build(), db,
            new TokenEncryptionService(new EphemeralDataProtectionProvider()), NullLogger<YouTubeService>.Instance, factory);

    private static async Task<ConnectedServiceAccount> AddAccountAsync(ApplicationDbContext db)
    {
        db.Users.Add(TestUserFactory.Create(701, "reconnect@example.test"));
        var account = new ConnectedServiceAccount { UserId = 701, Service = "youtube", ExternalAccountId = "same-account",
            ConnectionState = "connected", EncryptedRefreshToken = "refresh", TokenVersion = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.ConnectedServiceAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }
}
