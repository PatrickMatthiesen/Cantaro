using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class CantaroSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ReturnsOwnedRankedGroupsWithCanonicalRoutes()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(801, "search-owner@example.com");
        var other = TestUserFactory.Create(802, "search-other@example.com");
        db.Users.AddRange(owner, other);

        var exactTrack = MakeTrack("Echo", "First Artist", "https://img.example/echo.jpg", now);
        var prefixTrack = MakeTrack("Echoes", "Second Artist", null, now);
        var containsTrack = MakeTrack("Midnight Echo", "Third Artist", null, now);
        var artistTrack = MakeTrack("A Different Song", "Echo", null, now);
        var otherTrack = MakeTrack("Echo Private", "Other Artist", null, now);
        var unreachableTrack = MakeTrack("Echo Orphan", "Nobody", null, now);
        db.Tracks.AddRange(exactTrack, prefixTrack, containsTrack, artistTrack, otherTrack, unreachableTrack);

        var exactPlaylist = MakePlaylist(owner.Id, "Echo", "Exact playlist", now);
        var ownedPlaylist = MakePlaylist(owner.Id, "Echo collection", "An owned collection", now);
        var otherPlaylist = MakePlaylist(other.Id, "Echo private", "Must stay private", now);
        db.Playlists.AddRange(exactPlaylist, ownedPlaylist, otherPlaylist);
        await db.SaveChangesAsync();

        db.PlaylistEntries.AddRange(
            MakeEntry(exactPlaylist.Id, exactTrack.Id, 0, now),
            MakeEntry(exactPlaylist.Id, prefixTrack.Id, 1, now),
            MakeEntry(exactPlaylist.Id, containsTrack.Id, 2, now),
            MakeEntry(exactPlaylist.Id, artistTrack.Id, 3, now),
            MakeEntry(otherPlaylist.Id, otherTrack.Id, 0, now));

        var exactMedia = MakeMediaTitle("Echo", "anime", 2024, now);
        var prefixMedia = MakeMediaTitle("Echo Point", "movie", 2023, now);
        var otherMedia = MakeMediaTitle("Echo Private", "series", 2022, now);
        db.MediaTitles.AddRange(exactMedia, prefixMedia, otherMedia);
        await db.SaveChangesAsync();

        var exactMediaEntry = MakeMediaEntry(owner.Id, exactMedia.Id, "watching", now);
        var prefixMediaEntry = MakeMediaEntry(owner.Id, prefixMedia.Id, "planned", now);
        var otherMediaEntry = MakeMediaEntry(other.Id, otherMedia.Id, "watching", now);
        db.MediaLibraryEntries.AddRange(exactMediaEntry, prefixMediaEntry, otherMediaEntry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "  ECHO  ", 3, false, default);

        Assert.Equal("ECHO", result.Query);
        Assert.Equal(SearchGroupStatuses.Ok, result.Groups.Songs.Status);
        Assert.True(result.Groups.Songs.HasMore);
        Assert.Equal(
            [exactTrack.Id, prefixTrack.Id, containsTrack.Id],
            result.Groups.Songs.Items.Select(item => Guid.Parse(item.Id["track:".Length..])));
        var song = result.Groups.Songs.Items[0];
        Assert.Equal("song", song.EntityType);
        Assert.Equal("First Artist", song.Subtitle);
        Assert.Equal("https://img.example/echo.jpg", song.ArtworkUrl);
        Assert.Equal($"/music/songs/track%3A{exactTrack.Id}", song.CanonicalRoute);
        Assert.DoesNotContain(result.Groups.Songs.Items, item => item.Id == $"track:{otherTrack.Id}");
        Assert.DoesNotContain(result.Groups.Songs.Items, item => item.Id == $"track:{unreachableTrack.Id}");

        Assert.Equal(SearchGroupStatuses.Unavailable, result.Groups.Artists.Status);
        Assert.Empty(result.Groups.Artists.Items);
        Assert.False(result.Groups.Artists.HasMore);

        Assert.Equal([exactPlaylist.Id, ownedPlaylist.Id],
            result.Groups.Playlists.Items.Select(item => Guid.Parse(item.Id)));
        Assert.All(result.Groups.Playlists.Items,
            item => Assert.StartsWith("/music/playlists/", item.CanonicalRoute));
        Assert.DoesNotContain(result.Groups.Playlists.Items, item => item.Id == otherPlaylist.Id.ToString());

        Assert.Equal(
            [$"media-title:{exactMedia.Id}", $"media-title:{prefixMedia.Id}"],
            result.Groups.Media.Items.Select(item => item.Id));
        Assert.Equal("https://img.example/poster.jpg", result.Groups.Media.Items[0].ArtworkUrl);
        Assert.Equal($"/media/library/{exactMediaEntry.Id}", result.Groups.Media.Items[0].CanonicalRoute);
        Assert.True(result.Groups.Media.Items[0].IsInLibrary);
        Assert.Equal("watching", result.Groups.Media.Items[0].LibraryStatus);
        Assert.DoesNotContain(
            result.Groups.Media.Items,
            item => item.Id == $"media-title:{otherMedia.Id}");
    }

    [Fact]
    public async Task SearchAsync_MatchesOrderedTermsAcrossMediaTitleWords()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(812, "search-media-terms@example.com");
        var seasonThree = MakeMediaTitle(
            "That Time I Got Reincarnated as a Slime Season 3",
            "anime",
            2024,
            now);
        db.AddRange(owner, seasonThree);
        await db.SaveChangesAsync();
        db.MediaLibraryEntries.Add(MakeMediaEntry(owner.Id, seasonThree.Id, "planned", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "that time i 3", 8, false, default);

        var media = Assert.Single(result.Groups.Media.Items);
        Assert.Equal($"media-title:{seasonThree.Id}", media.Id);
        Assert.Equal("That Time I Got Reincarnated as a Slime Season 3", media.Title);
    }

    [Fact]
    public async Task SearchAsync_ClampsLimitAndUsesDeterministicTies()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(803, "search-limit@example.com");
        var playlist = MakePlaylist(owner.Id, "Library", null, now);
        db.AddRange(owner, playlist);

        var tracks = Enumerable.Range(0, 22)
            .Select(index => MakeTrack($"Same match {index:00}", "Artist", null, now))
            .ToList();
        db.Tracks.AddRange(tracks);
        await db.SaveChangesAsync();
        db.PlaylistEntries.AddRange(tracks.Select((track, index) => MakeEntry(playlist.Id, track.Id, index, now)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "same", 1000, false, default);

        Assert.Equal(CantaroSearchService.MaximumLimitPerGroup, result.Groups.Songs.Items.Count);
        Assert.True(result.Groups.Songs.HasMore);
        Assert.Equal(
            Enumerable.Range(0, CantaroSearchService.MaximumLimitPerGroup)
                .Select(index => $"Same match {index:00}"),
            result.Groups.Songs.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task SearchAsync_PropagatesCancellation()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(db).SearchAsync(1, "anything", 6, false, cancellation.Token));
    }

    [Fact]
    public async Task SearchAsync_ToleratesLegacyNullAlbumCollection()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(804, "search-legacy@example.com");
        var playlist = MakePlaylist(owner.Id, "Library", null, now);
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = """{"Title":"Legacy song","Artist":"Legacy artist","Albums":null}""",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AddRange(owner, playlist, track);
        await db.SaveChangesAsync();
        db.PlaylistEntries.Add(MakeEntry(playlist.Id, track.Id, 0, now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await CreateService(db).SearchAsync(owner.Id, "legacy", 6, false, default);

        var item = Assert.Single(result.Groups.Songs.Items);
        Assert.Equal("Legacy song", item.Title);
        Assert.Null(item.Detail);
    }

    [Fact]
    public async Task SearchAsync_ProviderDiscoveryRequiresConnectionAndExplicitFlagAndIsCached()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(805, "search-provider@example.com");
        var other = TestUserFactory.Create(811, "search-provider-other@example.com");
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync();

        var provider = new FakeMediaProvider(
            "anilist",
            [
                MakeProviderResult("anilist", "catalog-1", "Provider Echo"),
                MakeProviderResult("anilist", "catalog-2", "Provider Echo Two"),
                MakeProviderResult("anilist", "catalog-3", "Provider Echo Three")
            ]);
        using var cache = new MediaProviderSearchCache();
        var service = CreateService(db, cache, provider);

        var preview = await service.SearchAsync(owner.Id, "echo", 2, false, default);
        Assert.Empty(preview.Groups.Media.Items);
        Assert.Equal(0, provider.SearchCallCount);

        var disconnected = await service.SearchAsync(owner.Id, "echo", 2, true, default);
        Assert.Empty(disconnected.Groups.Media.Items);
        Assert.Equal(0, provider.SearchCallCount);

        db.ConnectedServiceAccounts.Add(MakeConnectedAccount(owner.Id, "ANILIST"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var first = await service.SearchAsync(owner.Id, "echo", 2, true, default);
        var second = await service.SearchAsync(owner.Id, "echo", 2, true, default);

        Assert.Equal(1, provider.SearchCallCount);
        Assert.Equal(2, first.Groups.Media.Items.Count);
        Assert.True(first.Groups.Media.HasMore);
        Assert.All(first.Groups.Media.Items, item =>
        {
            Assert.StartsWith("provider:anilist:", item.Id);
            Assert.StartsWith("/media/catalog/anilist/", item.CanonicalRoute);
            Assert.False(item.IsInLibrary);
        });
        Assert.Equal(first.Groups.Media.Items.Select(item => item.Id), second.Groups.Media.Items.Select(item => item.Id));
        Assert.Empty(await db.MediaTitles.ToListAsync());

        db.ConnectedServiceAccounts.Add(MakeConnectedAccount(other.Id, "anilist"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await service.SearchAsync(other.Id, "echo", 2, true, default);
        Assert.Equal(2, provider.SearchCallCount);

        await service.SearchAsync(owner.Id, "echo", 3, true, default);
        Assert.Equal(3, provider.SearchCallCount);
    }

    [Fact]
    public async Task SearchAsync_ShowsSharedCanonicalTitleThroughConnectedProviderWithoutLeakingOtherLibraryState()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var requester = TestUserFactory.Create(809, "search-shared@example.com");
        var other = TestUserFactory.Create(810, "search-shared-other@example.com");
        db.AddRange(
            requester,
            other,
            MakeConnectedAccount(requester.Id, "anilist"));
        var title = MakeMediaTitle("Shared Echo", "anime", 2025, now);
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();
        db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "shared-1",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.MediaLibraryEntries.Add(MakeMediaEntry(other.Id, title.Id, "dropped", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var provider = new FakeMediaProvider("anilist", []);
        using var cache = new MediaProviderSearchCache();
        var result = await CreateService(db, cache, provider)
            .SearchAsync(requester.Id, "shared", 6, false, default);

        var item = Assert.Single(result.Groups.Media.Items);
        Assert.Equal($"media-title:{title.Id}", item.Id);
        Assert.Equal("/media/catalog/anilist/shared-1", item.CanonicalRoute);
        Assert.False(item.IsInLibrary);
        Assert.Null(item.LibraryStatus);
        Assert.Null(item.Detail);
        Assert.Equal(0, provider.SearchCallCount);
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesByExactProviderIdentityAndUsesOwnedLibraryState()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var owner = TestUserFactory.Create(806, "search-dedup@example.com");
        var other = TestUserFactory.Create(807, "search-dedup-other@example.com");
        var account = MakeConnectedAccount(owner.Id, "anilist");
        db.AddRange(owner, other, account);

        var title = MakeMediaTitle("Echo Identity", "anime", 2024, now);
        db.MediaTitles.Add(title);
        await db.SaveChangesAsync();
        db.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = "anilist",
            ExternalId = "linked-1",
            LinkSource = MediaMappingSources.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        var connectedEntry = MakeMediaEntry(owner.Id, title.Id, "watching", now);
        connectedEntry.Provider = "anilist";
        connectedEntry.ProviderMediaId = "linked-1";
        connectedEntry.ConnectedServiceAccountId = account.Id;
        var olderDuplicate = MakeMediaEntry(owner.Id, title.Id, "completed", now.AddDays(-1));
        olderDuplicate.Provider = "legacy";
        var otherUserEntry = MakeMediaEntry(other.Id, title.Id, "dropped", now.AddDays(1));
        db.MediaLibraryEntries.AddRange(connectedEntry, olderDuplicate, otherUserEntry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var provider = new FakeMediaProvider(
            "anilist",
            [
                MakeProviderResult("anilist", "linked-1", "Different provider title"),
                MakeProviderResult("anilist", "LINKED-1", "Case-distinct provider identity"),
                // Same title/year is not identity and must remain a separate hit.
                MakeProviderResult("anilist", "unlinked-2", "Echo Identity")
            ]);
        using var cache = new MediaProviderSearchCache();
        var result = await CreateService(db, cache, provider)
            .SearchAsync(owner.Id, "echo", 6, true, default);

        Assert.Equal(3, result.Groups.Media.Items.Count);
        var canonical = Assert.Single(
            result.Groups.Media.Items,
            item => item.Id == $"media-title:{title.Id}");
        Assert.Equal($"/media/library/{connectedEntry.Id}", canonical.CanonicalRoute);
        Assert.True(canonical.IsInLibrary);
        Assert.Equal("watching", canonical.LibraryStatus);
        Assert.Equal("watching", canonical.Detail);
        Assert.DoesNotContain(result.Groups.Media.Items, item => item.Id.Contains("linked-1", StringComparison.Ordinal));
        Assert.Contains(result.Groups.Media.Items, item => item.Id == "provider:anilist:LINKED-1");
        Assert.Contains(result.Groups.Media.Items, item => item.Id == "provider:anilist:unlinked-2");
        Assert.DoesNotContain(result.Groups.Media.Items, item => item.Detail == "dropped");
    }

    [Fact]
    public async Task SearchAsync_IsolatesProviderFailureInsideMediaGroup()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var owner = TestUserFactory.Create(808, "search-provider-failure@example.com");
        db.Users.Add(owner);
        db.ConnectedServiceAccounts.AddRange(
            MakeConnectedAccount(owner.Id, "anilist"),
            MakeConnectedAccount(owner.Id, "mangadex"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var healthy = new FakeMediaProvider(
            "anilist",
            [MakeProviderResult("anilist", "healthy-1", "Healthy Echo")]);
        var failing = new FakeMediaProvider(
            "mangadex",
            [],
            new HttpRequestException("Provider unavailable"));
        using var cache = new MediaProviderSearchCache();

        var result = await CreateService(db, cache, healthy, failing)
            .SearchAsync(owner.Id, "echo", 6, true, default);

        Assert.Equal(SearchGroupStatuses.Ok, result.Groups.Media.Status);
        Assert.Single(result.Groups.Media.Items);
        Assert.Equal("One connected media provider could not be searched.", result.Groups.Media.Message);
    }

    [Fact]
    public async Task SearchAsync_FailsMediaGroupWhenEveryConnectedProviderFailsAndNothingIsUsable()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var owner = TestUserFactory.Create(812, "search-all-providers-fail@example.com");
        db.Users.Add(owner);
        db.ConnectedServiceAccounts.AddRange(
            MakeConnectedAccount(owner.Id, "anilist"),
            MakeConnectedAccount(owner.Id, "mangadex"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var first = new FakeMediaProvider(
            "anilist",
            [],
            new HttpRequestException("AniList unavailable"));
        var second = new FakeMediaProvider(
            "mangadex",
            [],
            new HttpRequestException("MangaDex unavailable"));
        using var cache = new MediaProviderSearchCache();

        var result = await CreateService(db, cache, first, second)
            .SearchAsync(owner.Id, "echo", 6, true, default);

        Assert.Equal(SearchGroupStatuses.Failed, result.Groups.Media.Status);
        Assert.Empty(result.Groups.Media.Items);
        Assert.False(result.Groups.Media.HasMore);
        Assert.Equal("Connected media providers could not be searched.", result.Groups.Media.Message);
    }

    [Fact]
    public async Task SaveChanges_DerivesSearchProjectionsAndToleratesMalformedMetadata()
    {
        var (db, connection) = await CreateDbAsync();
        await using var _ = connection;
        await using var __ = db;
        var now = DateTimeOffset.UtcNow;
        var track = MakeTrack("  Searchable title  ", "  Display artist  ", null, now);
        var malformed = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = "{broken",
            CreatedAt = now,
            UpdatedAt = now
        };
        var nonObject = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = "[]",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Tracks.AddRange(track, malformed, nonObject);

        await db.SaveChangesAsync();

        Assert.Equal("Searchable title", track.SearchTitle);
        Assert.Equal("Display artist", track.SearchArtist);
        Assert.Null(malformed.SearchTitle);
        Assert.Null(malformed.SearchArtist);
        Assert.Null(nonObject.SearchTitle);
        Assert.Null(nonObject.SearchArtist);
    }

    [Fact]
    public void SearchController_RequiresAuthorization()
    {
        var attribute = Assert.Single(
            typeof(Controllers.SearchController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
        Assert.IsType<AuthorizeAttribute>(attribute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchController_RejectsBlankQuery(string? query)
    {
        var controller = new Controllers.SearchController(null!, null!);

        var response = await controller.Search(query, 6);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task SearchController_RejectsOverlongQuery()
    {
        var controller = new Controllers.SearchController(null!, null!);

        var response = await controller.Search(
            new string('x', CantaroSearchService.MaximumQueryLength + 1),
            6);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    private static async Task<(ApplicationDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    private static CantaroSearchService CreateService(
        ApplicationDbContext db,
        params IMediaProvider[] providers) =>
        CreateService(db, new MediaProviderSearchCache(), providers);

    private static CantaroSearchService CreateService(
        ApplicationDbContext db,
        MediaProviderSearchCache cache,
        params IMediaProvider[] providers) =>
        new(
            db,
            new MediaProviderRegistry(providers),
            cache,
            NullLogger<CantaroSearchService>.Instance);

    private static Track MakeTrack(
        string title,
        string artist,
        string? artworkUrl,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = title,
                Artist = artist,
                Albums = ["Search album"],
                ThumbnailUrl = artworkUrl
            }),
            CreatedAt = now,
            UpdatedAt = now
        };

    private static Playlist MakePlaylist(
        int userId,
        string name,
        string? description,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static PlaylistEntry MakeEntry(
        Guid playlistId,
        Guid trackId,
        int position,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            PlaylistId = playlistId,
            TrackId = trackId,
            Position = position,
            AddedAt = now
        };

    private static MediaTitle MakeMediaTitle(
        string title,
        string kind,
        int startYear,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = title,
            MediaKind = kind,
            StartYear = startYear,
            PrimaryProgressDimension = "episodes",
            ReleaseStatusDimension = "episodes",
            CanonicalMetadata =
                """{"coverImage":{"medium":"https://img.example/medium.jpg","extraLarge":"https://img.example/poster.jpg"}}""",
            CreatedAt = now,
            UpdatedAt = now
        };

    private static MediaLibraryEntry MakeMediaEntry(
        int userId,
        Guid mediaTitleId,
        string status,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = mediaTitleId,
            Provider = "anilist",
            ProviderAccountId = $"account-{userId}",
            ProviderMediaId = Guid.NewGuid().ToString(),
            NormalizedStatus = status,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static ConnectedServiceAccount MakeConnectedAccount(int userId, string service) =>
        new()
        {
            UserId = userId,
            Service = service,
            ExternalAccountId = Guid.NewGuid().ToString(),
            ConnectionState = "connected",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static MediaProviderSearchResult MakeProviderResult(
        string providerId,
        string providerMediaId,
        string title) =>
        new()
        {
            ProviderId = providerId,
            ProviderMediaId = providerMediaId,
            Title = title,
            MediaKind = "anime",
            PosterUrl = "https://img.example/provider.jpg",
            StartYear = 2024,
            PrimaryProgressDimension = MediaProgressDimensions.Episode,
            ReleaseStatusDimension = MediaProgressDimensions.Episode
        };

    private sealed class FakeMediaProvider(
        string providerId,
        IReadOnlyList<MediaProviderSearchResult> searchResults,
        Exception? searchException = null) : IMediaProvider
    {
        public string ProviderId { get; } = providerId;
        public int SearchCallCount { get; private set; }

        public Task<IReadOnlyList<MediaProviderSearchResult>> SearchAsync(
            int userId,
            MediaCatalogSearchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchCallCount++;
            return searchException is null
                ? Task.FromResult(searchResults)
                : Task.FromException<IReadOnlyList<MediaProviderSearchResult>>(searchException);
        }

        public Task<ConnectedServiceAccount?> GetConnectedAccountAsync(
            int userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string GetAuthorizationUrl(string redirectUri, string state, string codeChallenge) =>
            throw new NotSupportedException();

        public Task<ConnectedServiceAccount> ExchangeCodeAndSaveAsync(
            int userId,
            string authorizationCode,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderLibraryImportResult> ImportLibraryAsync(
            int userId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderTitleDetails?> GetTitleDetailsAsync(
            int userId,
            string providerMediaId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateProgressAsync(
            int userId,
            MediaProgressUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaProviderMutationResult> UpdateStatusAsync(
            int userId,
            MediaStatusUpdateRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MediaReleaseMetadata?> GetReleaseMetadataAsync(
            int userId,
            string providerMediaId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
