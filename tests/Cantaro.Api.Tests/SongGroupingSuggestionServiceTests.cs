using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class SongGroupingSuggestionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Signal", "Signal (Acoustic)")]
    [InlineData("Signal", "Signal - Live")]
    [InlineData("Signal", "Signal (2026 Remaster)")]
    public async Task GenerateAsync_SuggestsDistinctVersionsForTheSameArtist(
        string anchorTitle,
        string candidateTitle)
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        var anchor = fixture.AddTrack(anchorTitle, "The Artist", "USAAA2600001");
        var candidate = fixture.AddTrack(candidateTitle, "The Artist", "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();

        var suggestions = await fixture.Service.GenerateAsync(CancellationToken.None);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(candidate.Track.Id, suggestion.CandidateTrackId);
        Assert.Equal(anchor.Song.Id, suggestion.TargetSongId);
        Assert.Equal(anchor.Track.Id, suggestion.AnchorTrackId);
        Assert.Equal(SongGroupingSuggestionStatuses.Pending, suggestion.Status);
        Assert.Contains("version", suggestion.EvidenceJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("All Time Low", "All Time Low")]
    [InlineData("All Time Low (Acoustic)", "All Time Low - Acoustic")]
    [InlineData("All Time Low (Live)", "All Time Low - Live")]
    public async Task GenerateAsync_DoesNotSuggestSameVersionRecordingDuplicates(
        string firstTitle,
        string secondTitle)
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack(firstTitle, "Jon Bellion", "USUM71504326");
        fixture.AddTrack(secondTitle, "Jon Bellion", "USUM71603666");
        await fixture.DbContext.SaveChangesAsync();

        Assert.Empty(await fixture.Service.GenerateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_DoesNotTreatTheSameIsrcAsSongGrouping()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack("I Like Me Better", "Lauv", "GBWWP1702907");
        fixture.AddTrack("I Like Me Better", "Lauv", "GBWWP1702907");
        await fixture.DbContext.SaveChangesAsync();

        var suggestions = await fixture.Service.GenerateAsync(CancellationToken.None);

        Assert.Empty(suggestions);
    }

    [Theory]
    [InlineData("Signal", "First Artist", "Signal", "Second Artist")]
    [InlineData("Signal", "The Artist", "Different Song", "The Artist")]
    public async Task GenerateAsync_DoesNotSuggestLookalikes(
        string firstTitle,
        string firstArtist,
        string secondTitle,
        string secondArtist)
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack(firstTitle, firstArtist, "USAAA2600001");
        fixture.AddTrack(secondTitle, secondArtist, "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();

        Assert.Empty(await fixture.Service.GenerateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_DoesNotSuggestCovers()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        fixture.AddTrack(
            "Signal (Cover)",
            "The Artist",
            "USAAA2600002",
            TrackVersionFlags.Cover);
        await fixture.DbContext.SaveChangesAsync();

        Assert.Empty(await fixture.Service.GenerateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_IsIdempotent()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        fixture.AddTrack("Signal (Acoustic)", "The Artist", "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Service.GenerateAsync(CancellationToken.None);
        fixture.DbContext.ChangeTracker.Clear();
        await fixture.Service.GenerateAsync(CancellationToken.None);

        Assert.Equal(1, await fixture.DbContext.SongGroupingSuggestions.CountAsync());
    }

    [Fact]
    public async Task GenerateAsync_ChoosesStudioAsTheSingleCanonicalTargetForVersionCluster()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        var acoustic = fixture.AddTrack("Signal (Acoustic)", "The Artist", "USAAA2600002");
        var live = fixture.AddTrack("Signal (Live)", "The Artist", "USAAA2600003");
        var studio = fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        await fixture.DbContext.SaveChangesAsync();

        var suggestions = await fixture.Service.GenerateAsync(CancellationToken.None);

        Assert.Equal(2, suggestions.Count);
        Assert.All(suggestions, suggestion =>
        {
            Assert.Equal(studio.Track.Id, suggestion.AnchorTrackId);
            Assert.Equal(studio.Song.Id, suggestion.TargetSongId);
        });
        Assert.Equal(
            new[] { acoustic.Track.Id, live.Track.Id }.Order().ToArray(),
            suggestions
                .Select(suggestion => suggestion.CandidateTrackId)
                .Order()
                .ToArray());
    }

    [Fact]
    public async Task GenerateAsync_DoesNotMoveTrackOutOfAnExistingGroupedSong()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        var candidate = fixture.AddTrack("Signal (Live)", "The Artist", "USAAA2600002");
        var existingVersion = fixture.AddTrack("Different Song", "The Artist", "USAAA2600003");
        fixture.DbContext.SongTracks.Add(new SongTrack
        {
            SongId = candidate.Song.Id,
            TrackId = existingVersion.Track.Id
        });
        await fixture.DbContext.SaveChangesAsync();

        Assert.Empty(await fixture.Service.GenerateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_AcceptMovesCandidateToTheTargetSong()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        var anchor = fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        var candidate = fixture.AddTrack("Signal (Acoustic)", "The Artist", "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();
        var suggestion = Assert.Single(
            await fixture.Service.GenerateAsync(CancellationToken.None));

        await fixture.Service.ReviewAsync(
            suggestion.Id,
            reviewerUserId: 42,
            accept: true,
            CancellationToken.None);

        var memberships = await fixture.DbContext.SongTracks
            .Where(membership => membership.TrackId == candidate.Track.Id)
            .ToListAsync();
        Assert.Single(memberships);
        Assert.Equal(anchor.Song.Id, memberships[0].SongId);
        var reviewed = await fixture.DbContext.SongGroupingSuggestions
            .SingleAsync(item => item.Id == suggestion.Id);
        Assert.Equal(SongGroupingSuggestionStatuses.Accepted, reviewed.Status);
        Assert.Equal(42, reviewed.ReviewedByUserId);
        Assert.NotNull(reviewed.AppliedAt);
        Assert.True(await fixture.DbContext.Songs.AnyAsync(song => song.Id == candidate.Song.Id));
    }

    [Fact]
    public async Task GenerateAsync_TargetsAnExistingGroupedSongForALaterVersion()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        var studio = fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        var acoustic = fixture.AddTrack(
            "Signal (Acoustic)",
            "The Artist",
            "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();
        var acousticSuggestion = Assert.Single(
            await fixture.Service.GenerateAsync(CancellationToken.None));
        await fixture.Service.ReviewAsync(
            acousticSuggestion.Id,
            reviewerUserId: 42,
            accept: true,
            CancellationToken.None);

        var live = fixture.AddTrack(
            "Signal (Live)",
            "The Artist",
            "USAAA2600003");
        await fixture.DbContext.SaveChangesAsync();

        var created = await fixture.Service.GenerateAsync(CancellationToken.None);

        var liveSuggestion = Assert.Single(created);
        Assert.Equal(live.Track.Id, liveSuggestion.CandidateTrackId);
        Assert.Equal(studio.Song.Id, liveSuggestion.TargetSongId);
        Assert.Equal(studio.Track.Id, liveSuggestion.AnchorTrackId);
        Assert.Equal(acoustic.Track.Id, acousticSuggestion.CandidateTrackId);
    }

    [Fact]
    public async Task ReviewAsync_RejectLeavesMembershipUnchanged()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        var candidate = fixture.AddTrack("Signal (Live)", "The Artist", "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();
        var suggestion = Assert.Single(
            await fixture.Service.GenerateAsync(CancellationToken.None));

        await fixture.Service.ReviewAsync(
            suggestion.Id,
            reviewerUserId: 42,
            accept: false,
            CancellationToken.None);

        Assert.True(await fixture.DbContext.SongTracks.AnyAsync(
            membership => membership.TrackId == candidate.Track.Id
                && membership.SongId == candidate.Song.Id));
        Assert.Equal(
            SongGroupingSuggestionStatuses.Rejected,
            (await fixture.DbContext.SongGroupingSuggestions.SingleAsync()).Status);
    }

    [Fact]
    public async Task ReviewAsync_FollowsAnchorToItsCurrentSong()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        var anchor = fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        var candidate = fixture.AddTrack("Signal (Acoustic)", "The Artist", "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();
        var suggestion = Assert.Single(
            await fixture.Service.GenerateAsync(CancellationToken.None));

        var currentAnchorSong = new Song
        {
            Id = Guid.NewGuid(),
            CreatedAt = Now.AddMinutes(1),
            UpdatedAt = Now.AddMinutes(1)
        };
        var staleMembership = await fixture.DbContext.SongTracks.SingleAsync(
            membership => membership.TrackId == anchor.Track.Id);
        fixture.DbContext.SongTracks.Remove(staleMembership);
        fixture.DbContext.AddRange(currentAnchorSong, new SongTrack
        {
            SongId = currentAnchorSong.Id,
            Song = currentAnchorSong,
            TrackId = anchor.Track.Id,
            Track = anchor.Track
        });
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Service.ReviewAsync(
            suggestion.Id,
            reviewerUserId: 42,
            accept: true,
            CancellationToken.None);

        Assert.True(await fixture.DbContext.SongTracks.AnyAsync(
            membership => membership.TrackId == candidate.Track.Id
                && membership.SongId == currentAnchorSong.Id));
        Assert.False(await fixture.DbContext.SongTracks.AnyAsync(
            membership => membership.TrackId == candidate.Track.Id
                && membership.SongId == anchor.Song.Id));
        var reviewed = await fixture.DbContext.SongGroupingSuggestions
            .SingleAsync(item => item.Id == suggestion.Id);
        Assert.Equal(currentAnchorSong.Id, reviewed.TargetSongId);
        Assert.Equal(SongGroupingSuggestionStatuses.Accepted, reviewed.Status);
    }

    [Fact]
    public async Task ReviewAsync_RejectsStaleSuggestionWhenSourceSongIsNoLongerSingleton()
    {
        await using var fixture = await GroupingFixture.CreateAsync();
        fixture.AddTrack("Signal", "The Artist", "USAAA2600001");
        var candidate = fixture.AddTrack("Signal (Live)", "The Artist", "USAAA2600002");
        await fixture.DbContext.SaveChangesAsync();
        var suggestion = Assert.Single(
            await fixture.Service.GenerateAsync(CancellationToken.None));
        var other = fixture.AddTrack("Different Song", "The Artist", "USAAA2600003");
        fixture.DbContext.SongTracks.Add(new SongTrack
        {
            SongId = candidate.Song.Id,
            TrackId = other.Track.Id
        });
        await fixture.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ReviewAsync(
                suggestion.Id,
                reviewerUserId: 42,
                accept: true,
                CancellationToken.None));

        fixture.DbContext.ChangeTracker.Clear();
        Assert.Equal(
            SongGroupingSuggestionStatuses.Pending,
            (await fixture.DbContext.SongGroupingSuggestions.SingleAsync()).Status);
    }

    private sealed class GroupingFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private int _trackCount;

        private GroupingFixture(
            SqliteConnection connection,
            ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = new SongGroupingSuggestionService(dbContext);
        }

        public ApplicationDbContext DbContext { get; }
        public SongGroupingSuggestionService Service { get; }

        public static async Task<GroupingFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Users.Add(new User
            {
                Id = 42,
                UserName = "reviewer@example.test",
                NormalizedUserName = "REVIEWER@EXAMPLE.TEST",
                Email = "reviewer@example.test",
                NormalizedEmail = "REVIEWER@EXAMPLE.TEST",
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            });
            await dbContext.SaveChangesAsync();
            return new GroupingFixture(connection, dbContext);
        }

        public (Song Song, Track Track) AddTrack(
            string title,
            string artist,
            string isrc,
            TrackVersionFlags versionFlags = TrackVersionFlags.None)
        {
            var createdAt = Now.AddSeconds(_trackCount++);
            var song = new Song
            {
                Id = Guid.NewGuid(),
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };
            var track = new Track
            {
                Id = Guid.NewGuid(),
                Isrc = isrc,
                VersionFlags = versionFlags,
                CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
                {
                    Title = title,
                    Artist = artist,
                    DurationSeconds = 180
                }),
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };
            DbContext.AddRange(song, track, new SongTrack
            {
                SongId = song.Id,
                Song = song,
                TrackId = track.Id,
                Track = track
            });
            return (song, track);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
