using System.Security.Claims;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ProfileControllerTests
{
    [Fact]
    public async Task GetProfileCreatesDefaultsAndDerivesDisplayName()
    {
        await using var fixture = await ProfileFixture.CreateAsync();

        var result = await fixture.Controller.GetProfile(CancellationToken.None);

        var profile = Assert.IsType<ProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("listener", profile.DisplayName);
        Assert.Equal(UserThemePreferences.System, profile.Preferences.Theme);
        Assert.True(profile.Preferences.KeepPlaylistOrder);
        Assert.Single(fixture.Db.UserSettings);
    }

    [Fact]
    public async Task UpdatePreferencesRejectsUnknownTheme()
    {
        await using var fixture = await ProfileFixture.CreateAsync();

        var result = await fixture.Controller.UpdatePreferences(new UpdateProfilePreferencesRequest
        {
            Theme = "neon",
            KeepPlaylistOrder = true,
            KeepPlaylistMetadata = true,
            HideUnavailableTracks = true,
            ScheduledSync = true
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AvatarUploadUsesVersionedObjectAndReturnsCacheHeaders()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "avatar", "avatar.webp") { Headers = new HeaderDictionary(), ContentType = "image/webp" };

        var upload = await fixture.Controller.UploadAvatar(file, CancellationToken.None);

        var profile = Assert.IsType<ProfileDto>(Assert.IsType<OkObjectResult>(upload.Result).Value);
        Assert.Contains("/api/profile/avatar?v=", profile.AvatarUrl);
        Assert.StartsWith($"avatars/{fixture.User.Id}/", Assert.Single(fixture.Store.Objects).Key);

        var avatar = await fixture.Controller.GetAvatar(null, CancellationToken.None);
        Assert.Equal("image/webp", Assert.IsType<FileContentResult>(avatar).ContentType);
        Assert.Equal("private, max-age=31536000, immutable", fixture.HttpContext.Response.Headers.CacheControl);
        Assert.Equal("nosniff", fixture.HttpContext.Response.Headers.XContentTypeOptions);
        Assert.Equal("\"test-etag\"", fixture.HttpContext.Response.Headers.ETag);

        fixture.HttpContext.Request.Headers.IfNoneMatch = "\"test-etag\"";
        var notModified = await fixture.Controller.GetAvatar(profile.AvatarUrl!.Split('=')[1], CancellationToken.None);
        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(notModified).StatusCode);
        Assert.Equal("private, max-age=31536000, immutable", fixture.HttpContext.Response.Headers.CacheControl);

        Assert.IsType<NotFoundResult>(await fixture.Controller.GetAvatar(Guid.NewGuid().ToString("N"), CancellationToken.None));
    }

    [Fact]
    public async Task AvatarUploadAcceptsJpegAndPngFallbacks()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };
        await using var jpegStream = new MemoryStream(jpegBytes);
        var jpegFile = new FormFile(jpegStream, 0, jpegBytes.Length, "avatar", "avatar.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };

        var jpegUpload = await fixture.Controller.UploadAvatar(jpegFile, CancellationToken.None);

        Assert.IsType<OkObjectResult>(jpegUpload.Result);
        var jpegObject = Assert.Single(fixture.Store.Objects);
        Assert.EndsWith(".jpg", jpegObject.Key);
        Assert.Equal("image/jpeg", jpegObject.Value.ContentType);
        Assert.Equal("image/jpeg", Assert.IsType<FileContentResult>(await fixture.Controller.GetAvatar(null, CancellationToken.None)).ContentType);

        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        await using var pngStream = new MemoryStream(pngBytes);
        var pngFile = new FormFile(pngStream, 0, pngBytes.Length, "avatar", "avatar.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };

        var pngUpload = await fixture.Controller.UploadAvatar(pngFile, CancellationToken.None);

        Assert.IsType<OkObjectResult>(pngUpload.Result);
        var pngObject = Assert.Single(fixture.Store.Objects);
        Assert.EndsWith(".png", pngObject.Key);
        Assert.Equal("image/png", pngObject.Value.ContentType);
        Assert.Equal("image/png", Assert.IsType<FileContentResult>(await fixture.Controller.GetAvatar(null, CancellationToken.None)).ContentType);
    }

    [Fact]
    public async Task AvatarUploadRejectsUnsupportedContent()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var file = new FormFile(stream, 0, stream.Length, "avatar", "avatar.webp") { Headers = new HeaderDictionary(), ContentType = "image/webp" };

        var result = await fixture.Controller.UploadAvatar(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(fixture.Store.Objects);
    }

    [Fact]
    public async Task AvatarUploadRejectsContentTypeMismatch()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };
        await using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "avatar", "avatar.webp") { Headers = new HeaderDictionary(), ContentType = "image/webp" };

        var result = await fixture.Controller.UploadAvatar(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(fixture.Store.Objects);
    }

    [Fact]
    public async Task ReplacingAvatarDeletesPreviousObject()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 };

        await using var firstStream = new MemoryStream(bytes);
        var firstFile = new FormFile(firstStream, 0, bytes.Length, "avatar", "first.webp")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/webp"
        };
        await fixture.Controller.UploadAvatar(firstFile, CancellationToken.None);
        var previousObjectKey = Assert.Single(fixture.Store.Objects).Key;

        await using var replacementStream = new MemoryStream(bytes);
        var replacementFile = new FormFile(replacementStream, 0, bytes.Length, "avatar", "replacement.webp")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/webp"
        };
        await fixture.Controller.UploadAvatar(replacementFile, CancellationToken.None);

        var replacementObjectKey = Assert.Single(fixture.Store.Objects).Key;
        Assert.NotEqual(previousObjectKey, replacementObjectKey);
        Assert.False(fixture.Store.Objects.ContainsKey(previousObjectKey));
    }

    private sealed class ProfileFixture : IAsyncDisposable
    {
        private ProfileFixture(SqliteConnection connection, ApplicationDbContext db, User user, FakeAvatarStore store, DefaultHttpContext httpContext, ProfileController controller)
        {
            Connection = connection;
            Db = db;
            User = user;
            Store = store;
            HttpContext = httpContext;
            Controller = controller;
        }

        public SqliteConnection Connection { get; }
        public ApplicationDbContext Db { get; }
        public User User { get; }
        public FakeAvatarStore Store { get; }
        public DefaultHttpContext HttpContext { get; }
        public ProfileController Controller { get; }

        public static async Task<ProfileFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var userManager = CreateUserManager(db);
            var user = new User { UserName = "listener@example.com", Email = "listener@example.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            Assert.True((await userManager.CreateAsync(user, "secret1")).Succeeded);
            var store = new FakeAvatarStore();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"))
            };
            var controller = new ProfileController(db, userManager, null!, store, NullLogger<ProfileController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
            return new ProfileFixture(connection, db, user, store, httpContext, controller);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }

        private static UserManager<User> CreateUserManager(ApplicationDbContext db)
        {
            var store = new UserStore<User, IdentityRole<int>, ApplicationDbContext, int>(db);
            return new UserManager<User>(store, Options.Create(new IdentityOptions
            {
                Password = { RequireDigit = false, RequireLowercase = false, RequireUppercase = false, RequireNonAlphanumeric = false, RequiredLength = 6 }
            }), new PasswordHasher<User>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<User>>.Instance);
        }
    }

    private sealed class FakeAvatarStore : IAvatarStore
    {
        public Dictionary<string, StoredAvatar> Objects { get; } = [];

        public async Task<string> PutAsync(string objectKey, Stream content, long length, string contentType, CancellationToken cancellationToken)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Objects[objectKey] = new StoredAvatar(buffer.ToArray(), contentType, "test-etag");
            return "test-etag";
        }

        public Task<StoredAvatar?> GetAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(Objects.GetValueOrDefault(objectKey));

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Objects.Remove(objectKey);
            return Task.CompletedTask;
        }
    }
}
