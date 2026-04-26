using System.Net;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class AniListMediaProviderTests
{
    [Fact]
    public async Task DisconnectAsync_RemovesCredentialsAndLeavesEntriesDisconnected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var user = TestUserFactory.Create(301, "disconnect@example.com");
        var account = new ConnectedServiceAccount
        {
            Id = 901,
            UserId = user.Id,
            Service = "anilist",
            ExternalAccountId = "viewer-301",
            DisplayName = "Disconnect Tester",
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Apothecary Diaries",
            MediaKind = MediaKinds.Anime,
            SupportsEpisodeProgress = true,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode,
            CreatedAt = now,
            UpdatedAt = now
        };
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MediaTitleId = title.Id,
            ConnectedServiceAccountId = account.Id,
            Provider = "anilist",
            ProviderAccountId = account.ExternalAccountId,
            ProviderMediaId = "161645",
            NormalizedStatus = MediaLibraryStatuses.Current,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Users.Add(user);
        dbContext.ConnectedServiceAccounts.Add(account);
        dbContext.MediaTitles.Add(title);
        dbContext.MediaLibraryEntries.Add(entry);
        await dbContext.SaveChangesAsync();

        var provider = CreateProvider(dbContext);
        await provider.DisconnectAsync(user.Id, CancellationToken.None);

        var persistedEntry = await dbContext.MediaLibraryEntries.SingleAsync();

        Assert.Empty(dbContext.ConnectedServiceAccounts);
        Assert.Null(persistedEntry.ConnectedServiceAccountId);
        Assert.Equal(MediaMutationSources.ProviderDisconnect, persistedEntry.LastMutationSource);
    }

    private static AniListMediaProvider CreateProvider(ApplicationDbContext dbContext)
    {
        var apiClient = new AniListApiClient(
            new HttpClient(new StubHttpMessageHandler()),
            Options.Create(new AniListOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }),
            NullLogger<AniListApiClient>.Instance);
        var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var tokenEncryption = new TokenEncryptionService(dataProtectionProvider);

        return new AniListMediaProvider(dbContext, apiClient, tokenEncryption, NullLogger<AniListMediaProvider>.Instance);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
