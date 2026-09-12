using System.Security.Claims;
using Cantaro.Api.Configuration;
using CantaroAuthenticationOptions = Cantaro.Api.Configuration.AuthenticationOptions;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class GoogleAuthServiceTests
{
    [Fact]
    public async Task CompleteLoginAsync_SignsInExistingProviderSubject()
    {
        await using var fixture = await CreateFixtureAsync();
        var user = await fixture.CreateUserAsync("existing@example.com", "Password1!");
        await fixture.AddGoogleLoginAsync(user, "google-subject-1");
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("google-subject-1", user.Email!);
        fixture.SetCurrentUser(null);
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLoginChallenge("/");

        var result = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(GoogleAuthOperation.Login, result.Operation);
        Assert.Equal(user.Id, fixture.SignInManager.LastSignedInUser?.Id);
    }

    [Fact]
    public async Task CompleteCallbackAsync_RejectsUnverifiedGoogleEmail()
    {
        await using var fixture = await CreateFixtureAsync();
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("unverified-subject", "unverified@example.com", verified: false);
        fixture.SetCurrentUser(null);
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLoginChallenge("/");

        var result = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_email_unverified", result.ErrorCode);
        Assert.Null(fixture.SignInManager.LastSignedInUser);
    }

    [Fact]
    public async Task CompleteLoginAsync_RejectsEmailCollisionWithoutMergingAccounts()
    {
        await using var fixture = await CreateFixtureAsync();
        var existingUser = await fixture.CreateUserAsync("collision@example.com", "Password1!");
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("new-google-subject", existingUser.Email!);
        fixture.SetCurrentUser(null);
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLoginChallenge("/");

        var result = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_email_collision", result.ErrorCode);
        Assert.Null(fixture.SignInManager.LastSignedInUser);
        Assert.Empty(await fixture.Users.GetLoginsAsync(existingUser));
        Assert.Equal(1, await fixture.Db.Users.CountAsync());
    }

    [Fact]
    public async Task StartLinkAsync_RequiresTheCurrentPasswordForPasswordAccounts()
    {
        await using var fixture = await CreateFixtureAsync();
        var user = await fixture.CreateUserAsync("link@example.com", "Password1!");

        var missingPassword = await fixture.Service.StartLinkAsync(user, null, "/settings", CancellationToken.None);
        var wrongPassword = await fixture.Service.StartLinkAsync(user, "WrongPassword1!", "/settings", CancellationToken.None);

        Assert.False(missingPassword.Succeeded);
        Assert.Equal("fresh_auth_required", missingPassword.ErrorCode);
        Assert.False(wrongPassword.Succeeded);
        Assert.Equal("invalid_current_password", wrongPassword.ErrorCode);
    }

    [Fact]
    public async Task CompleteLinkAsync_RequiresTheSameSignedInUserAsTheGrant()
    {
        await using var fixture = await CreateFixtureAsync();
        var grantOwner = await fixture.CreateUserAsync("grant-owner@example.com", "Password1!");
        var otherUser = await fixture.CreateUserAsync("other-user@example.com", "Password1!");
        var start = await fixture.Service.StartLinkAsync(grantOwner, "Password1!", "/settings", CancellationToken.None);
        var grant = ReadToken(start.AuthorizationUrl!);

        fixture.SetCurrentUser(otherUser);
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("link-subject", "linked@example.com");
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLinkChallenge(grant);

        var result = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fresh_auth_required", result.ErrorCode);
        Assert.Empty(await fixture.Users.GetLoginsAsync(grantOwner));
        Assert.Empty(await fixture.Users.GetLoginsAsync(otherUser));
    }

    [Fact]
    public async Task CompleteLinkAsync_RejectsExpiredAndReplayedGrants()
    {
        await using var fixture = await CreateFixtureAsync();
        var user = await fixture.CreateUserAsync("grant@example.com", "Password1!");
        fixture.SetCurrentUser(user);

        var expiredStart = await fixture.Service.StartLinkAsync(user, "Password1!", "/settings", CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromMinutes(6));
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("expired-subject", "expired@example.com");
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLinkChallenge(ReadToken(expiredStart.AuthorizationUrl!));

        var expiredResult = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.False(expiredResult.Succeeded);
        Assert.Equal("google_callback_invalid", expiredResult.ErrorCode);

        var validStart = await fixture.Service.StartLinkAsync(user, "Password1!", "/settings", CancellationToken.None);
        var validGrant = ReadToken(validStart.AuthorizationUrl!);
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("replay-subject", "replay@example.com");
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLinkChallenge(validGrant);

        var firstResult = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("replay-subject", "replay@example.com");
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateLinkChallenge(validGrant);
        var replayResult = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.True(firstResult.Succeeded);
        Assert.Equal(GoogleAuthOperation.Link, firstResult.Operation);
        Assert.False(replayResult.Succeeded);
        Assert.Equal("google_callback_invalid", replayResult.ErrorCode);
    }

    [Fact]
    public async Task CompleteReauthenticationAsync_RejectsAProviderBelongingToAnotherUser()
    {
        await using var fixture = await CreateFixtureAsync();
        var currentUser = await fixture.CreateUserAsync("reauth-current@example.com", "Password1!");
        var otherUser = await fixture.CreateUserAsync("reauth-other@example.com", "Password1!");
        await fixture.AddGoogleLoginAsync(currentUser, "current-google-subject");
        await fixture.AddGoogleLoginAsync(otherUser, "other-google-subject");
        fixture.SetCurrentUser(currentUser);

        var start = await fixture.Service.StartReauthenticationAsync(currentUser, "/settings", CancellationToken.None);
        fixture.SignInManager.ExternalLogin = CreateExternalLogin("other-google-subject", otherUser.Email!);
        fixture.SignInManager.ExternalLogin.AuthenticationProperties = fixture.Service.CreateReauthenticationChallenge(ReadToken(start.AuthorizationUrl!));

        var result = await fixture.Service.CompleteCallbackAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("google_reauth_mismatch", result.ErrorCode);
        Assert.Empty(fixture.SignInManager.LastClaims);
    }

    private static ExternalLoginInfo CreateExternalLogin(string subject, string email, bool verified = true)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, email),
            new Claim("email_verified", verified ? "true" : "false")
        ],
        authenticationType: GoogleDefaults.AuthenticationScheme));

        return new ExternalLoginInfo(principal, GoogleDefaults.AuthenticationScheme, subject, "Google");
    }

    private static string ReadToken(string authorizationUrl)
    {
        var query = QueryHelpers.ParseQuery(new Uri(new Uri("https://cantaro.example"), authorizationUrl).Query);
        return query["token"].Single()!;
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"google-auth-{Guid.NewGuid():N}"));
        services.AddIdentityCore<User>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddAuthentication()
            .AddCookie(IdentityConstants.ExternalScheme);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var time = new TestTimeProvider(DateTimeOffset.UtcNow);
        var signInManager = new StubSignInManager(
            userManager,
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(),
            scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<User>>(),
            scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>(),
            NullLogger<SignInManager<User>>.Instance,
            scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>(),
            scope.ServiceProvider.GetRequiredService<IUserConfirmation<User>>());
        var service = new GoogleAuthService(
            userManager,
            signInManager,
            Options.Create(new CantaroAuthenticationOptions { Mode = AuthenticationMode.Both }),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
            time);

        return new TestFixture(provider, scope, userManager, scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), signInManager, service, time);
    }

    private sealed class TestFixture(
        ServiceProvider provider,
        IServiceScope scope,
        UserManager<User> users,
        ApplicationDbContext db,
        StubSignInManager signInManager,
        GoogleAuthService service,
        TestTimeProvider time) : IAsyncDisposable
    {
        public UserManager<User> Users { get; } = users;
        public ApplicationDbContext Db { get; } = db;
        public StubSignInManager SignInManager { get; } = signInManager;
        public GoogleAuthService Service { get; } = service;
        public TestTimeProvider Time { get; } = time;

        public async Task<User> CreateUserAsync(string email, string? password)
        {
            var user = new User
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var result = password is null
                ? await Users.CreateAsync(user)
                : await Users.CreateAsync(user, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
            return user;
        }

        public async Task AddGoogleLoginAsync(User user, string subject)
        {
            var result = await Users.AddLoginAsync(user, CreateExternalLogin(subject, user.Email!));
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        public void SetCurrentUser(User? user)
        {
            var principal = user is null
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
                    authenticationType: "Identity.Application"));
            SignInManager.ContextAccessor.HttpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = principal
            };
        }

        public ValueTask DisposeAsync()
        {
            scope.Dispose();
            provider.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubSignInManager(
        UserManager<User> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<User> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<User>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<User> confirmation)
        : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        public IHttpContextAccessor ContextAccessor { get; } = contextAccessor;
        public ExternalLoginInfo? ExternalLogin { get; set; }
        public User? LastSignedInUser { get; private set; }
        public IReadOnlyList<Claim> LastClaims { get; private set; } = [];

        public override AuthenticationProperties ConfigureExternalAuthenticationProperties(string? provider, string? redirectUrl, string? userId = null) =>
            new() { RedirectUri = redirectUrl };

        public override Task<ExternalLoginInfo?> GetExternalLoginInfoAsync(string? expectedXsrf = null) =>
            Task.FromResult(ExternalLogin);

        public override Task SignInAsync(User user, bool isPersistent, string? authenticationMethod = null)
        {
            LastSignedInUser = user;
            return Task.CompletedTask;
        }

        public override Task RefreshSignInAsync(User user)
        {
            LastSignedInUser = user;
            return Task.CompletedTask;
        }

        public override Task SignInWithClaimsAsync(User user, bool isPersistent, IEnumerable<Claim> additionalClaims)
        {
            LastSignedInUser = user;
            LastClaims = additionalClaims.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime;

        public override DateTimeOffset GetUtcNow() => _currentTime;

        public void Advance(TimeSpan duration) => _currentTime += duration;
    }
}
