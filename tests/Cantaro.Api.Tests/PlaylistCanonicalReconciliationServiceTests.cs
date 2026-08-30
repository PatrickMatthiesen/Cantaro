using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistCanonicalReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileImportedEntries_SameCanonicalTrack_RetainsEarliestPresentation()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();
        var track = fixture.AddTrack(TrackVersionFlags.None);
        var firstObservation = fixture.AddObservation("video", track.Id);
        var secondObservation = fixture.AddObservation("lyrics", track.Id);
        var entries = new[]
        {
            fixture.CreateEntry(firstObservation.Id, track.Id, 0),
            fixture.CreateEntry(secondObservation.Id, track.Id, 1)
        };

        var retained = fixture.Service.ReconcileImportedEntries(entries);
        fixture.DbContext.PlaylistEntries.AddRange(retained);
        await fixture.DbContext.SaveChangesAsync();

        var entry = Assert.Single(await fixture.DbContext.PlaylistEntries.AsNoTracking().ToListAsync());
        Assert.Equal(firstObservation.Id, entry.TrackObservationId);
        Assert.Equal(track.Id, entry.TrackId);
        Assert.Equal(0, entry.Position);
        Assert.Equal(2, await fixture.DbContext.TrackObservations.CountAsync());
    }

    [Fact]
    public async Task ReconcileImportedEntries_DistinctVersionsOfSameSong_RetainsBothTracks()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();
        var song = new Song { Id = Guid.NewGuid(), CreatedAt = fixture.Now, UpdatedAt = fixture.Now };
        var baseTrack = fixture.AddTrack(TrackVersionFlags.None);
        var slowedTrack = fixture.AddTrack(TrackVersionFlags.Slowed);
        fixture.DbContext.AddRange(
            song,
            new SongTrack { SongId = song.Id, Song = song, TrackId = baseTrack.Id, Track = baseTrack },
            new SongTrack { SongId = song.Id, Song = song, TrackId = slowedTrack.Id, Track = slowedTrack });
        var baseObservation = fixture.AddObservation("base", baseTrack.Id);
        var slowedObservation = fixture.AddObservation("slowed", slowedTrack.Id);
        var entries = new[]
        {
            fixture.CreateEntry(baseObservation.Id, baseTrack.Id, 0),
            fixture.CreateEntry(slowedObservation.Id, slowedTrack.Id, 1)
        };

        var retained = fixture.Service.ReconcileImportedEntries(entries);
        fixture.DbContext.PlaylistEntries.AddRange(retained);
        await fixture.DbContext.SaveChangesAsync();

        var persisted = await fixture.DbContext.PlaylistEntries.AsNoTracking()
            .OrderBy(entry => entry.Position)
            .ToListAsync();
        Assert.Equal(2, persisted.Count);
        Assert.Equal([baseTrack.Id, slowedTrack.Id], persisted.Select(entry => entry.TrackId));
        Assert.Equal([0, 1], persisted.Select(entry => entry.Position));
    }

    [Fact]
    public async Task ReconcileImportedEntries_RepeatedUnresolvedSource_RetainsOneOccurrence()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();
        var observation = fixture.AddObservation("repeated", trackId: null);
        var entries = new[]
        {
            fixture.CreateEntry(observation.Id, trackId: null, position: 4),
            fixture.CreateEntry(observation.Id, trackId: null, position: 9)
        };

        var retained = fixture.Service.ReconcileImportedEntries(entries);
        fixture.DbContext.PlaylistEntries.AddRange(retained);
        await fixture.DbContext.SaveChangesAsync();

        var entry = Assert.Single(await fixture.DbContext.PlaylistEntries.AsNoTracking().ToListAsync());
        Assert.Equal(observation.Id, entry.TrackObservationId);
        Assert.Null(entry.TrackId);
        Assert.Equal(0, entry.Position);
        Assert.Equal(1, await fixture.DbContext.TrackObservations.CountAsync());
    }

    [Fact]
    public async Task ReconcileImportedEntries_RemovesMiddleDuplicateAndCompactsPositions()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();
        var firstTrack = fixture.AddTrack(TrackVersionFlags.None);
        var secondTrack = fixture.AddTrack(TrackVersionFlags.Slowed);
        var firstObservation = fixture.AddObservation("first", firstTrack.Id);
        var duplicateObservation = fixture.AddObservation("duplicate", firstTrack.Id);
        var secondObservation = fixture.AddObservation("second", secondTrack.Id);
        var entries = new[]
        {
            fixture.CreateEntry(firstObservation.Id, firstTrack.Id, 0),
            fixture.CreateEntry(duplicateObservation.Id, firstTrack.Id, 1),
            fixture.CreateEntry(secondObservation.Id, secondTrack.Id, 2)
        };

        var retained = fixture.Service.ReconcileImportedEntries(entries);
        fixture.DbContext.PlaylistEntries.AddRange(retained);
        await fixture.DbContext.SaveChangesAsync();

        var persisted = await fixture.DbContext.PlaylistEntries.AsNoTracking()
            .OrderBy(entry => entry.Position)
            .ToListAsync();
        Assert.Equal(2, persisted.Count);
        Assert.Equal([0, 1], persisted.Select(entry => entry.Position));
        Assert.Equal([firstTrack.Id, secondTrack.Id], persisted.Select(entry => entry.TrackId));
    }

    private sealed class ReconciliationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ReconciliationFixture(SqliteConnection connection, ApplicationDbContext dbContext, Playlist playlist)
        {
            _connection = connection;
            DbContext = dbContext;
            Playlist = playlist;
            Service = new PlaylistCanonicalReconciliationService(dbContext);
        }

        public DateTimeOffset Now { get; } = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        public ApplicationDbContext DbContext { get; }
        public Playlist Playlist { get; }
        public PlaylistCanonicalReconciliationService Service { get; }

        public static async Task<ReconciliationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
            var user = new User
            {
                Id = 902,
                UserName = "reconciliation-test",
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime
            };
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                Name = "Canonical reconciliation",
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.AddRange(user, playlist);
            return new ReconciliationFixture(connection, dbContext, playlist);
        }

        public Track AddTrack(TrackVersionFlags versionFlags)
        {
            var track = new Track
            {
                Id = Guid.NewGuid(),
                VersionFlags = versionFlags,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            DbContext.Tracks.Add(track);
            return track;
        }

        public TrackObservation AddObservation(string externalId, Guid? trackId)
        {
            var observation = new TrackObservation
            {
                Id = Guid.NewGuid(),
                SourceType = "youtube",
                ExternalId = externalId,
                Title = externalId,
                MatchStatus = trackId.HasValue ? TrackMatchingStatuses.Matched : TrackMatchingStatuses.Pending,
                TrackId = trackId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            DbContext.TrackObservations.Add(observation);
            return observation;
        }

        public PlaylistEntry CreateEntry(Guid observationId, Guid? trackId, int position) => new()
        {
            Id = Guid.NewGuid(),
            PlaylistId = Playlist.Id,
            TrackObservationId = observationId,
            TrackId = trackId,
            Position = position,
            AddedAt = Now,
            SourceService = "youtube"
        };

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
