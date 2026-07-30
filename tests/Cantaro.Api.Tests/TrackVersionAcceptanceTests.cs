using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackVersionAcceptanceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptCandidateAsVersionAsync_CreatesAnchorAndSpedUpTrackInOneSong()
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.AcceptCandidateAsVersionAsync(
            setup.Observation.Id,
            setup.Candidate.Id,
            TrackVersionFlags.SpedUp,
            CancellationToken.None);

        var versionTrack = await fixture.DbContext.Tracks
            .SingleAsync(track => track.Id == result.TrackId);
        var anchorTrack = await fixture.DbContext.Tracks
            .SingleAsync(track => track.MbidRecording == setup.Candidate.MbidRecording);
        var versionMetadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(
            versionTrack.CanonicalMetadata!);

        Assert.NotEqual(anchorTrack.Id, versionTrack.Id);
        Assert.Equal(TrackVersionFlags.SpedUp, versionTrack.VersionFlags);
        Assert.Null(versionTrack.MbidRecording);
        Assert.Null(versionTrack.Isrc);
        Assert.Equal("GUY.exe", versionMetadata?.Title);
        Assert.Equal("Superfruit", versionMetadata?.Artist);
        Assert.Equal(185, versionMetadata?.DurationSeconds);
        using var evidence = JsonDocument.Parse(versionTrack.VersionEvidence!);
        Assert.Equal(
            "manual-candidate-version",
            evidence.RootElement.GetProperty("origin").GetString());
        Assert.Equal(
            setup.Observation.Id.ToString(),
            evidence.RootElement.GetProperty("observationId").GetString());
        Assert.Equal(
            setup.Candidate.Id.ToString(),
            evidence.RootElement.GetProperty("candidateId").GetString());
        Assert.Contains(
            "speed up",
            versionTrack.VersionEvidence!,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal("mb-guy-exe", anchorTrack.MbidRecording);
        Assert.Equal("USRC17607839", anchorTrack.Isrc);
        Assert.Equal(2, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(1, await fixture.DbContext.Songs.CountAsync());
        Assert.Equal(2, await fixture.DbContext.SongTracks.CountAsync());
        var memberships = await fixture.DbContext.SongTracks.ToListAsync();
        Assert.Single(memberships.Select(membership => membership.SongId).Distinct());

        Assert.Equal(
            ["youtube"],
            await fixture.DbContext.TrackSourceIds
                .Where(source => source.TrackId == versionTrack.Id)
                .Select(source => source.SourceType)
                .ToArrayAsync());
        Assert.Equal(
            ["musicbrainz"],
            await fixture.DbContext.TrackSourceIds
                .Where(source => source.TrackId == anchorTrack.Id)
                .Select(source => source.SourceType)
                .ToArrayAsync());
        Assert.Equal(versionTrack.Id, setup.PlaylistEntry.TrackId);
        Assert.True(setup.Candidate.IsAccepted);
        Assert.Equal(setup.Candidate.Id, result.AcceptedCandidateId);
        Assert.Contains("sped-up version", result.ResolutionNotes!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptCandidateAsVersionAsync_ReusesExistingCandidateAnchorAndSong()
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        var anchor = fixture.AddExistingAnchor(setup.Candidate);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.AcceptCandidateAsVersionAsync(
            setup.Observation.Id,
            setup.Candidate.Id,
            TrackVersionFlags.SpedUp,
            CancellationToken.None);

        Assert.NotEqual(anchor.Track.Id, result.TrackId);
        Assert.Equal(2, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(1, await fixture.DbContext.Songs.CountAsync());
        Assert.Equal(
            anchor.Song.Id,
            await fixture.DbContext.SongTracks
                .Where(membership => membership.TrackId == result.TrackId)
                .Select(membership => membership.SongId)
            .SingleAsync());
    }

    [Fact]
    public async Task AcceptCandidateAsVersionAsync_IsIdempotentForTheSameResolution()
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        await fixture.DbContext.SaveChangesAsync();

        var first = await fixture.Service.AcceptCandidateAsVersionAsync(
            setup.Observation.Id,
            setup.Candidate.Id,
            TrackVersionFlags.SpedUp,
            CancellationToken.None);
        fixture.DbContext.ChangeTracker.Clear();

        var repeated = await fixture.Service.AcceptCandidateAsVersionAsync(
            setup.Observation.Id,
            setup.Candidate.Id,
            TrackVersionFlags.SpedUp,
            CancellationToken.None);

        Assert.Equal(first.TrackId, repeated.TrackId);
        Assert.Equal(2, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(1, await fixture.DbContext.Songs.CountAsync());
        Assert.Equal(2, await fixture.DbContext.SongTracks.CountAsync());
        Assert.Equal(
            1,
            await fixture.DbContext.TrackSourceIds.CountAsync(source =>
                source.SourceType == "youtube"
                && source.ExternalId == setup.Observation.ExternalId));
    }

    [Fact]
    public async Task AcceptCandidateAsVersionAsync_DoesNotTreatAnUnrelatedMatchedTrackAsIdempotent()
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        var unrelatedTrack = new Track
        {
            Id = Guid.NewGuid(),
            VersionFlags = TrackVersionFlags.SpedUp,
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "GUY.exe",
                Artist = "Superfruit",
                DurationSeconds = 185
            }),
            CreatedAt = Now,
            UpdatedAt = Now
        };
        setup.Observation.TrackId = unrelatedTrack.Id;
        setup.Observation.MatchStatus = TrackMatchingStatuses.Matched;
        setup.Observation.AcceptedCandidateId = setup.Candidate.Id;
        fixture.DbContext.Tracks.Add(unrelatedTrack);
        await fixture.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.AcceptCandidateAsVersionAsync(
                setup.Observation.Id,
                setup.Candidate.Id,
                TrackVersionFlags.SpedUp,
                CancellationToken.None));

        Assert.Single(await fixture.DbContext.Tracks.ToListAsync());
    }

    [Fact]
    public async Task AcceptCandidateAsVersionAsync_ReusesExistingAnchorByIsrc()
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        setup.Candidate.MbidRecording = null;
        setup.Candidate.ExternalId = "different-candidate-source-id";
        var anchor = fixture.AddExistingAnchor(
            setup.Candidate,
            addSourceMapping: false);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.AcceptCandidateAsVersionAsync(
            setup.Observation.Id,
            setup.Candidate.Id,
            TrackVersionFlags.SpedUp,
            CancellationToken.None);

        Assert.NotEqual(anchor.Track.Id, result.TrackId);
        Assert.Equal(2, await fixture.DbContext.Tracks.CountAsync());
        Assert.Equal(anchor.Track.Id, await fixture.DbContext.Tracks
            .Where(track => track.Isrc == setup.Candidate.Isrc)
            .Select(track => track.Id)
            .SingleAsync());
        Assert.Equal(
            anchor.Song.Id,
            await fixture.DbContext.SongTracks
                .Where(membership => membership.TrackId == result.TrackId)
                .Select(membership => membership.SongId)
                .SingleAsync());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L << 30)]
    public async Task AcceptCandidateAsVersionAsync_RejectsInvalidClassificationWithoutMutation(
        long rawFlags)
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        await fixture.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Service.AcceptCandidateAsVersionAsync(
                setup.Observation.Id,
                setup.Candidate.Id,
                (TrackVersionFlags)rawFlags,
                CancellationToken.None));

        Assert.Empty(await fixture.DbContext.Tracks.ToListAsync());
        Assert.Empty(await fixture.DbContext.Songs.ToListAsync());
        Assert.Empty(await fixture.DbContext.SongTracks.ToListAsync());
        Assert.Empty(await fixture.DbContext.TrackSourceIds.ToListAsync());
        Assert.Equal(TrackMatchingStatuses.Ambiguous, setup.Observation.MatchStatus);
        Assert.Null(setup.Observation.TrackId);
        Assert.False(setup.Candidate.IsAccepted);
        Assert.Null(setup.PlaylistEntry.TrackId);
    }

    [Fact]
    public async Task CreateTrackFromObservationAsync_PersistsDetectedSpedUpClassification()
    {
        await using var fixture = await VersionFixture.CreateAsync();
        var setup = fixture.AddGuyExeObservation();
        fixture.DbContext.TrackResolutionCandidates.Remove(setup.Candidate);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.Service.CreateTrackFromObservationAsync(
            setup.Observation.Id,
            CancellationToken.None);

        var track = await fixture.DbContext.Tracks.SingleAsync(
            candidate => candidate.Id == result.TrackId);
        Assert.Equal(TrackVersionFlags.SpedUp, track.VersionFlags);
        Assert.Contains("speed up", track.VersionEvidence!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class VersionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private VersionFixture(
            SqliteConnection connection,
            ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = new TrackMatchingService(
                dbContext,
                [],
                NullLogger<TrackMatchingService>.Instance);
        }

        public ApplicationDbContext DbContext { get; }
        public TrackMatchingService Service { get; }

        public static async Task<VersionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            return new VersionFixture(connection, dbContext);
        }

        public VersionSetup AddGuyExeObservation()
        {
            var user = TestUserFactory.Create(2001, "version-review@example.test");
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = "Løb",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var observation = new TrackObservation
            {
                Id = Guid.NewGuid(),
                SourceType = "youtube",
                ExternalId = "CH_oVqS6iss",
                Title = "Superfruit - Guy.exe speed up",
                Artist = "Iztuwa",
                ThumbnailUrl = "https://i.ytimg.com/vi/CH_oVqS6iss/mqdefault.jpg",
                DurationSeconds = 185,
                MatchStatus = TrackMatchingStatuses.Ambiguous,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var candidate = new TrackResolutionCandidate
            {
                Id = Guid.NewGuid(),
                TrackObservationId = observation.Id,
                CandidateSource = "musicbrainz",
                ExternalId = "mb-guy-exe",
                Title = "GUY.exe",
                Artist = "Superfruit",
                MbidRecording = "mb-guy-exe",
                Isrc = "USRC17607839",
                DurationSeconds = 223,
                Score = 0.65m,
                RawMetadata = "{}",
                CreatedAt = Now
            };
            var playlistEntry = new PlaylistEntry
            {
                Id = Guid.NewGuid(),
                PlaylistId = playlist.Id,
                TrackObservationId = observation.Id,
                Position = 27,
                AddedAt = Now
            };

            DbContext.AddRange(user, playlist, observation, candidate, playlistEntry);
            return new VersionSetup(observation, candidate, playlistEntry);
        }

        public (Song Song, Track Track) AddExistingAnchor(
            TrackResolutionCandidate candidate,
            bool addSourceMapping = true)
        {
            var song = new Song
            {
                Id = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var track = new Track
            {
                Id = Guid.NewGuid(),
                MbidRecording = candidate.MbidRecording,
                Isrc = candidate.Isrc,
                CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
                {
                    Title = candidate.Title,
                    Artist = candidate.Artist,
                    DurationSeconds = candidate.DurationSeconds
                }),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            DbContext.AddRange(
                song,
                track,
                new SongTrack
                {
                    SongId = song.Id,
                    Song = song,
                    TrackId = track.Id,
                    Track = track
                });
            if (addSourceMapping)
            {
                DbContext.TrackSourceIds.Add(new TrackSourceId
                {
                    Id = Guid.NewGuid(),
                    TrackId = track.Id,
                    SourceType = "musicbrainz",
                    ExternalId = candidate.ExternalId
                });
            }
            return (song, track);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed record VersionSetup(
        TrackObservation Observation,
        TrackResolutionCandidate Candidate,
        PlaylistEntry PlaylistEntry);
}
