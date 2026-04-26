using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class ExtensionAuthServiceTests
{
    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_IssuesJwtAndRefreshToken()
    {
        await using var database = await CreateDatabaseAsync();
        await using var dbContext = database.CreateDbContext();
        var user = new User
        {
            UserName = "extension-user",
            Email = "extension@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string clientId = "abcdefghijklmnopqrstuvwxzy123456";
        const string redirectUri = "https://abcdefghijklmnopqrstuvwxzy123456.chromiumapp.org/cantaro-auth";
        const string codeVerifier = "test-code-verifier-1234567890";
        var codeChallenge = CreatePkceChallenge(codeVerifier);

        var authorizationCode = await service.CreateAuthorizationCodeAsync(
            user,
            clientId,
            redirectUri,
            codeChallenge,
            CancellationToken.None);
        var tokenResponse = await service.ExchangeAuthorizationCodeAsync(
            authorizationCode,
            clientId,
            redirectUri,
            codeVerifier,
            CancellationToken.None);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenResponse.AccessToken);

        Assert.Equal("Bearer", tokenResponse.TokenType);
        Assert.NotEmpty(tokenResponse.RefreshToken);
        Assert.True(tokenResponse.ExpiresIn > 0);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, claim => claim.Type == "client_id" && claim.Value == clientId);
        Assert.Single(await dbContext.ExtensionRefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_RotatesStoredRefreshTokens()
    {
        await using var database = await CreateDatabaseAsync();
        await using var dbContext = database.CreateDbContext();
        var user = new User
        {
            UserName = "refresh-user",
            Email = "refresh@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        const string clientId = "abcdefghijklmnopqrstuvwxyzabcdef";
        const string redirectUri = "https://abcdefghijklmnopqrstuvwxyzabcdef.chromiumapp.org/callback";
        const string codeVerifier = "refresh-verifier-1234567890";
        var authorizationCode = await service.CreateAuthorizationCodeAsync(
            user,
            clientId,
            redirectUri,
            CreatePkceChallenge(codeVerifier),
            CancellationToken.None);
        var initialTokenResponse = await service.ExchangeAuthorizationCodeAsync(
            authorizationCode,
            clientId,
            redirectUri,
            codeVerifier,
            CancellationToken.None);

        var refreshedTokenResponse = await service.RefreshAccessTokenAsync(
            initialTokenResponse.RefreshToken,
            clientId,
            CancellationToken.None);

        var refreshTokens = await dbContext.ExtensionRefreshTokens
            .OrderBy(token => token.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, refreshTokens.Count);
        Assert.NotEqual(initialTokenResponse.RefreshToken, refreshedTokenResponse.RefreshToken);
        Assert.NotNull(refreshTokens[0].RevokedAt);
        Assert.NotNull(refreshTokens[0].LastUsedAt);
        Assert.Null(refreshTokens[1].RevokedAt);
    }

    [Fact]
    public async Task ExchangeAuthorizationCodeAsync_WorksAcrossSeparateServiceInstances()
    {
        await using var database = await CreateDatabaseAsync();
        await using var seedContext = database.CreateDbContext();
        var user = new User
        {
            UserName = "multi-instance-user",
            Email = "multi-instance@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();

        const string clientId = "extensionclientabcdefghijklmnop";
        const string redirectUri = "https://extensionclientabcdefghijklmnop.chromiumapp.org/callback";
        const string codeVerifier = "multi-instance-verifier-1234567890";
        var codeChallenge = CreatePkceChallenge(codeVerifier);

        string authorizationCode;
        await using (var issuingContext = database.CreateDbContext())
        {
            var issuingService = CreateService(issuingContext);
            authorizationCode = await issuingService.CreateAuthorizationCodeAsync(
                user,
                clientId,
                redirectUri,
                codeChallenge,
                CancellationToken.None);
        }

        await using var redeemingContext = database.CreateDbContext();
        var redeemingService = CreateService(redeemingContext);

        var tokenResponse = await redeemingService.ExchangeAuthorizationCodeAsync(
            authorizationCode,
            clientId,
            redirectUri,
            codeVerifier,
            CancellationToken.None);

        Assert.NotEmpty(tokenResponse.AccessToken);
        Assert.NotEmpty(tokenResponse.RefreshToken);
    }

    private static ExtensionAuthService CreateService(ApplicationDbContext dbContext)
    {
        return new ExtensionAuthService(
            dbContext,
            new ExtensionAuthorizationCodeStore(dbContext),
            Options.Create(new ExtensionAuthOptions
            {
                JwtIssuer = "Cantaro.Tests",
                JwtAudience = "cantaro-browser-extension-tests",
                JwtSigningKey = "Cantaro.Test.Extension.Auth.Signing.Key.2026.04.26",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 30,
                AuthorizationCodeLifetimeMinutes = 5
            }),
            new TestWebHostEnvironment());
    }

    private static async Task<DatabaseFixture> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var dbContext = new ApplicationDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        return new DatabaseFixture(connection, options);
    }

    private static string CreatePkceChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        return WebEncoders.Base64UrlEncode(hash);
    }

    private sealed record DatabaseFixture(SqliteConnection Connection, DbContextOptions<ApplicationDbContext> Options) : IAsyncDisposable
    {
        public ApplicationDbContext CreateDbContext() => new(Options);

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Cantaro.Api.Tests";

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}