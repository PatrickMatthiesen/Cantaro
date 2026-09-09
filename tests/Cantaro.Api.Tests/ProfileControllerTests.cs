using System.IO.Compression;
using System.Security.Claims;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
        Assert.Equal(MediaReleaseTrackPreferences.Default, profile.Preferences.PreferredMediaReleaseTrack);
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
            ScheduledSync = true,
            BlurEmailAddress = false
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdatePreferencesNormalizesPreferredMediaReleaseTrack()
    {
        await using var fixture = await ProfileFixture.CreateAsync();

        var result = await fixture.Controller.UpdatePreferences(new UpdateProfilePreferencesRequest
        {
            Theme = UserThemePreferences.Dark,
            KeepPlaylistOrder = true,
            KeepPlaylistMetadata = true,
            HideUnavailableTracks = true,
            ScheduledSync = true,
            BlurEmailAddress = false,
            PreferredMediaReleaseTrack = "DUB:pt-br"
        }, CancellationToken.None);

        var profile = Assert.IsType<ProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("dub:pt-BR", profile.Preferences.PreferredMediaReleaseTrack);
        Assert.Equal("dub:pt-BR", (await fixture.Db.UserSettings.SingleAsync()).PreferredMediaReleaseTrack);
    }

    [Theory]
    [InlineData("raw:en")]
    [InlineData("sub")]
    [InlineData("dub:")]
    [InlineData("dub:english")]
    public async Task UpdatePreferencesRejectsInvalidPreferredMediaReleaseTrack(string preferredMediaReleaseTrack)
    {
        await using var fixture = await ProfileFixture.CreateAsync();

        var result = await fixture.Controller.UpdatePreferences(new UpdateProfilePreferencesRequest
        {
            Theme = UserThemePreferences.Dark,
            KeepPlaylistOrder = true,
            KeepPlaylistMetadata = true,
            HideUnavailableTracks = true,
            ScheduledSync = true,
            BlurEmailAddress = false,
            PreferredMediaReleaseTrack = preferredMediaReleaseTrack
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

    [Fact]
    public async Task ExportContainsPendingMatchingTasksRegardlessOfAgeWithoutRawPayloads()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        fixture.Db.MediaObservations.Add(new MediaObservation
        {
            UserId = fixture.User.Id,
            SiteIdentifier = "crunchyroll",
            ObservedUrl = "https://www.crunchyroll.com/watch/episode",
            SiteMediaId = "series-1",
            ObservedTitle = "Episode",
            ProgressHint = "Episode 1",
            ObservedAt = now,
            SeriesTitle = "Series",
            EpisodeTitle = "Episode",
            EpisodeNumber = 1,
            ProviderSeriesId = "series-1",
            ReleaseTrack = "sub:en",
            MatchStatus = MediaObservationStatuses.NoMatch,
            CreatedAt = now,
            UpdatedAt = now
        });
        fixture.Db.MediaObservations.Add(new MediaObservation
        {
            UserId = fixture.User.Id,
            SiteIdentifier = "crunchyroll",
            ObservedUrl = "https://www.crunchyroll.com/watch/expired",
            SiteMediaId = "expired-series",
            ObservedTitle = "older unresolved observation",
            ObservedAt = now.AddDays(-31),
            MatchStatus = MediaObservationStatuses.NoMatch,
            CreatedAt = now.AddDays(-31),
            UpdatedAt = now.AddDays(-31)
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.Export(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        await using var archiveStream = new MemoryStream(file.FileContents);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var dataEntry = Assert.Single(archive.Entries, entry => entry.FullName == "cantaro-export.json");
        using var reader = new StreamReader(dataEntry.Open());
        var json = await reader.ReadToEndAsync();

        Assert.Contains("series-1", json);
        Assert.Contains("Episode", json);
        Assert.Contains("older unresolved observation", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtensionVersion", json, StringComparison.Ordinal);
        Assert.DoesNotContain("RawPayload", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderChoicesPayload", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolutionHistoryPayload", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteAccountRemovesPersonalDataAndLeavesCanonicalMediaData()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            CanonicalTitle = "Canonical series",
            MediaKind = "anime",
            PrimaryProgressDimension = "episodes",
            ReleaseStatusDimension = "episodes"
        };
        var providerLink = new MediaProviderLink
        {
            MediaTitle = title,
            Provider = "anilist",
            ExternalId = "123",
            LinkSource = "provider"
        };
        fixture.Db.MediaTitles.Add(title);
        fixture.Db.MediaProviderLinks.Add(providerLink);
        fixture.Db.ExtensionAuthorizationCodes.Add(new ExtensionAuthorizationCode
        {
            UserId = fixture.User.Id,
            ClientId = "cantaro-extension",
            RedirectUri = "https://extension.invalid/callback",
            CodeChallenge = "challenge",
            CodeHash = "code-hash",
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.AddMinutes(5).UtcDateTime
        });
        fixture.Db.ExtensionRefreshTokens.Add(new ExtensionRefreshToken
        {
            UserId = fixture.User.Id,
            ClientId = "cantaro-extension",
            TokenHash = "refresh-hash",
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.AddDays(30).UtcDateTime
        });
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            UserId = fixture.User.Id,
            MediaTitle = title,
            Status = "watching",
            CreatedAt = now,
            UpdatedAt = now
        });
        var observation = new MediaObservation
        {
            UserId = fixture.User.Id,
            SiteIdentifier = "crunchyroll",
            ObservedUrl = "https://www.crunchyroll.com/watch/episode",
            SiteMediaId = "series-1",
            ObservedTitle = "Episode",
            ObservedAt = now,
            MediaTitle = title,
            MatchStatus = MediaObservationStatuses.Matched,
            CreatedAt = now,
            UpdatedAt = now
        };
        observation.Candidates.Add(new MediaObservationCandidate
        {
            CandidateSource = "provider_search",
            MediaTitle = title,
            Title = title.CanonicalTitle,
            MediaKind = title.MediaKind,
            Score = 0.95m
        });
        observation.Episodes.Add(new MediaObservationEpisode
        {
            ProviderEpisodeId = "episode-1",
            ProviderUrl = "https://www.crunchyroll.com/watch/episode",
            EpisodeNumber = 1,
            EpisodeTitle = "Episode",
            ReleaseTrack = "sub:en",
            AvailableSubtitleLanguageCodes = ["en"],
            AvailableAudioLanguageCodes = []
        });
        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Controller.DeleteAccount(
            new DeleteAccountRequest { CurrentPassword = "secret1" },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(fixture.Db.Users);
        Assert.Empty(fixture.Db.UserSettings);
        Assert.Empty(fixture.Db.MediaLibraryEntries);
        Assert.Empty(fixture.Db.MediaObservations);
        Assert.Empty(fixture.Db.MediaObservationCandidates);
        Assert.Empty(fixture.Db.Set<MediaObservationEpisode>());
        Assert.Empty(fixture.Db.ExtensionAuthorizationCodes);
        Assert.Empty(fixture.Db.ExtensionRefreshTokens);
        Assert.Single(fixture.Db.MediaTitles);
        Assert.Single(fixture.Db.MediaProviderLinks);
    }

    [Fact]
    public async Task RetentionDeletesDismissedTasksAndKeepsOldUnresolvedTasks()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var rejected = CreateObservation(fixture.User.Id, "rejected", now, now);
        rejected.MatchStatus = MediaObservationStatuses.Rejected;
        var pending = CreateObservation(fixture.User.Id, "pending", now.AddDays(-90), default);
        fixture.Db.MediaObservations.AddRange(rejected, pending);
        await fixture.Db.SaveChangesAsync();
        var deleted = await new MediaObservationRetentionService(fixture.Db).CleanupCompletedAsync();
        Assert.Equal(1, deleted);
        Assert.Equal("pending", await fixture.Db.MediaObservations.Select(o => o.SiteMediaId).SingleAsync());
    }

    [Fact]
    public async Task RetentionVerifiesSavedProgressBeforeDeletingLegacyResolvedTasks()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            CanonicalTitle = "Series", MediaKind = "anime",
            PrimaryProgressDimension = "episodes", ReleaseStatusDimension = "episodes"
        };
        var entry = new MediaLibraryEntry
        {
            UserId = fixture.User.Id, MediaTitle = title, Status = "watching", ProgressEpisodes = 5
        };
        fixture.Db.MediaLibraryEntries.Add(entry);
        await fixture.Db.SaveChangesAsync();
        var completed = CreateObservation(fixture.User.Id, "completed", now, now);
        var interrupted = CreateObservation(fixture.User.Id, "interrupted", now.AddDays(-90), now.AddDays(-90));
        foreach (var observation in new[] { completed, interrupted })
        {
            observation.MatchStatus = MediaObservationStatuses.Matched;
            observation.MediaTitleId = title.Id;
            observation.ResolvedLibraryEntryId = entry.Id;
        }
        completed.ResolvedProgress = 5;
        interrupted.ResolvedProgress = 6;
        fixture.Db.MediaObservations.AddRange(completed, interrupted);
        await fixture.Db.SaveChangesAsync();
        var deleted = await new MediaObservationRetentionService(fixture.Db).CleanupCompletedAsync();
        Assert.Equal(1, deleted);
        Assert.Equal("interrupted", await fixture.Db.MediaObservations.Select(o => o.SiteMediaId).SingleAsync());
        Assert.Equal(5, await fixture.Db.MediaLibraryEntries.Select(e => e.ProgressEpisodes).SingleAsync());
    }

    [Fact]
    public async Task RetentionUsesProgressHintWhenLegacyMatchedTaskHasNoResolvedLibraryEntry()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var title = new MediaTitle
        {
            CanonicalTitle = "Progress hint series",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        fixture.Db.MediaLibraryEntries.Add(new MediaLibraryEntry
        {
            UserId = fixture.User.Id,
            MediaTitle = title,
            Status = MediaLibraryStatuses.Current,
            ProgressEpisodes = 7
        });
        var observation = CreateObservation(fixture.User.Id, "legacy-auto", now, now);
        observation.MatchStatus = MediaObservationStatuses.Matched;
        observation.MediaTitle = title;
        observation.ProgressHint = "7";
        observation.ResolvedProgress = null;
        observation.ResolvedLibraryEntryId = null;
        fixture.Db.MediaObservations.Add(observation);
        await fixture.Db.SaveChangesAsync();

        var deleted = await new MediaObservationRetentionService(fixture.Db).CleanupCompletedAsync();

        Assert.Equal(1, deleted);
        Assert.Empty(await fixture.Db.MediaObservations.ToListAsync());
    }

    [Fact]
    public async Task RetentionKeepsConflictedCatalogEvidenceAndDeletesFullyRecordedCatalogEvidence()
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var targetTitle = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Catalog target",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        var conflictingTitle = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Catalog conflict target",
            MediaKind = MediaKinds.Anime,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };
        var targetEpisode = new MediaEpisode
        {
            Id = Guid.NewGuid(),
            MediaTitle = targetTitle,
            MediaTitleId = targetTitle.Id,
            EpisodeNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var conflictingEpisode = new MediaEpisode
        {
            Id = Guid.NewGuid(),
            MediaTitle = conflictingTitle,
            MediaTitleId = conflictingTitle.Id,
            EpisodeNumber = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var recordedContent = new MediaEpisodeProviderContent
        {
            Id = Guid.NewGuid(),
            MediaEpisode = targetEpisode,
            MediaEpisodeId = targetEpisode.Id,
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            ProviderContentKey = "recorded-content"
        };
        var conflictedContent = new MediaEpisodeProviderContent
        {
            Id = Guid.NewGuid(),
            MediaEpisode = conflictingEpisode,
            MediaEpisodeId = conflictingEpisode.Id,
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            ProviderContentKey = "conflicted-content"
        };
        var recordedIdentity = CreateProviderIdentity(
            recordedContent,
            "RECORDED",
            now,
            hasConflict: false);
        var conflictedIdentity = CreateProviderIdentity(
            conflictedContent,
            "CONFLICTED",
            now,
            hasConflict: true);
        var retained = CreateObservation(fixture.User.Id, "catalog-conflict", now, now);
        retained.MatchStatus = MediaObservationStatuses.Matched;
        retained.IsCatalogObservation = true;
        retained.MediaTitle = targetTitle;
        retained.Episodes.Add(new MediaObservationEpisode
        {
            ProviderEpisodeId = "CONFLICTED",
            ProviderUrl = "https://www.crunchyroll.com/watch/CONFLICTED",
            EpisodeNumber = 1
        });
        var removable = CreateObservation(fixture.User.Id, "catalog-recorded", now, now);
        removable.MatchStatus = MediaObservationStatuses.Matched;
        removable.IsCatalogObservation = true;
        removable.MediaTitle = targetTitle;
        removable.Episodes.Add(new MediaObservationEpisode
        {
            ProviderEpisodeId = "RECORDED",
            ProviderUrl = "https://www.crunchyroll.com/watch/RECORDED",
            EpisodeNumber = 1
        });
        fixture.Db.AddRange(
            targetTitle,
            conflictingTitle,
            targetEpisode,
            conflictingEpisode,
            recordedContent,
            conflictedContent,
            recordedIdentity,
            conflictedIdentity,
            retained,
            removable);
        await fixture.Db.SaveChangesAsync();

        var deleted = await new MediaObservationRetentionService(fixture.Db).CleanupCompletedAsync();

        Assert.Equal(1, deleted);
        Assert.Equal("catalog-conflict", await fixture.Db.MediaObservations.Select(item => item.SiteMediaId).SingleAsync());
    }

    private static MediaEpisodeProviderIdentity CreateProviderIdentity(
        MediaEpisodeProviderContent content,
        string providerEpisodeId,
        DateTimeOffset now,
        bool hasConflict) => new()
        {
            Id = Guid.NewGuid(),
            Content = content,
            MediaEpisodeProviderContentId = content.Id,
            Provider = MediaObservationSiteIdentifiers.Crunchyroll,
            ProviderEpisodeId = providerEpisodeId,
            ProviderUrlPath = $"/watch/{providerEpisodeId}",
            SeenCount = 1,
            FirstSeenAt = now,
            LastSeenAt = now,
            HasConflict = hasConflict
        };

    private static MediaObservation CreateObservation(int userId, string siteMediaId, DateTimeOffset createdAt, DateTimeOffset updatedAt) => new()
    {
        UserId = userId,
        SiteIdentifier = "crunchyroll",
        ObservedUrl = $"https://www.crunchyroll.com/watch/{siteMediaId}",
        SiteMediaId = siteMediaId,
        ObservedTitle = siteMediaId,
        ObservedAt = createdAt,
        MatchStatus = MediaObservationStatuses.Pending,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    };

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
            var db = new ProfileDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var userManager = CreateUserManager(db);
            var user = new User { UserName = "listener@example.com", Email = "listener@example.com", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            Assert.True((await userManager.CreateAsync(user, "secret1")).Succeeded);
            var store = new FakeAvatarStore();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"))
            };
            var identityOptions = Options.Create(new IdentityOptions());
            var signInManager = new TestSignInManager(
                userManager,
                new HttpContextAccessor { HttpContext = httpContext },
                new UserClaimsPrincipalFactory<User>(userManager, identityOptions),
                identityOptions,
                NullLogger<SignInManager<User>>.Instance,
                new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions())),
                new DefaultUserConfirmation<User>());
            var controller = new ProfileController(db, userManager, signInManager, store, NullLogger<ProfileController>.Instance)
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

    /// SQLite does not support ordering DateTimeOffset values directly. Keep
    /// this fixture's timestamps as UTC DateTime values so the retention and
    /// export predicates exercise their real SQL paths.
    private sealed class ProfileDbContext(DbContextOptions<ApplicationDbContext> options)
        : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(new ValueConverter<DateTimeOffset, DateTime>(
                            value => value.UtcDateTime,
                            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))));
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(new ValueConverter<DateTimeOffset?, DateTime?>(
                            value => value.HasValue ? value.Value.UtcDateTime : null,
                            value => value.HasValue
                                ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                                : null));
                    }
                }
            }
        }
    }

    private sealed class TestSignInManager(
        UserManager<User> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<User> claimsFactory,
        IOptions<IdentityOptions> options,
        Microsoft.Extensions.Logging.ILogger<SignInManager<User>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<User> confirmation)
        : SignInManager<User>(userManager, contextAccessor, claimsFactory, options, logger, schemes, confirmation)
    {
        public override Task SignOutAsync() => Task.CompletedTask;
    }
}
