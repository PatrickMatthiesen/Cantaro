using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public class TrackMatchingServiceTests
{
    [Fact]
    public async Task ProcessObservationAsync_PersistsNewCandidatesAsInserts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-1",
            Title = "Lil Nas X - STAR WALKIN' (League of Legends Worlds Anthem)",
            Artist = "League of Legends",
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "candidate-1",
                Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
                Artist = "Lil Nas X",
                ArtistMusicBrainzId = "151eeb9d-4e8c-4823-b2a0-4a9c9c0e2f2f",
                ArtistSortName = "Lil Nas X",
                MbidRecording = "candidate-1",
                Isrc = "USSM12208809",
                DurationSeconds = 211,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "candidate-2",
                Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
                Artist = "Lil Nas X",
                MbidRecording = "candidate-2",
                Isrc = "USSM12208810",
                DurationSeconds = 210,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Equal(2, await dbContext.TrackResolutionCandidates.CountAsync());
    }

    [Fact]
    public async Task ProcessObservationAsync_AutoMatchUsesPersistedCandidate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-2",
            Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
            Artist = "Lil Nas X",
            DurationSeconds = 211,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "candidate-1",
                Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
                Artist = "Lil Nas X",
                ArtistMusicBrainzId = "151eeb9d-4e8c-4823-b2a0-4a9c9c0e2f2f",
                ArtistSortName = "Lil Nas X",
                MbidRecording = "candidate-1",
                Isrc = "USSM12208809",
                DurationSeconds = 211,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);
        var acceptedCandidateId = result.AcceptedCandidateId;
        await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var acceptedCandidate = await dbContext.TrackResolutionCandidates.SingleAsync();

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.Equal(acceptedCandidate.Id, acceptedCandidateId);
        Assert.True(acceptedCandidate.IsAccepted);
        Assert.NotNull(result.TrackId);
        var credit = await dbContext.TrackArtistCredits.Include(item => item.Artist).SingleAsync();
        Assert.Equal(TrackArtistRole.Primary, credit.Role);
        Assert.Equal("Lil Nas X", credit.CreditedName);
        Assert.Equal("151eeb9d-4e8c-4823-b2a0-4a9c9c0e2f2f", credit.Artist?.MusicBrainzArtistId);
        Assert.Single(await dbContext.Artists.ToListAsync());
    }

    [Fact]
    public async Task ProcessObservationAsync_ExistingSourceBackfillsCanonicalArtistCreditIdempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var track = new Track
        {
            Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            {
                Title = "Existing song",
                Artist = "Canonical Artist"
            }),
            CreatedAt = now,
            UpdatedAt = now
        };
        track.SourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(),
            TrackId = track.Id,
            SourceType = "youtube",
            ExternalId = "existing-video"
        });
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "existing-video",
            Title = "Existing song",
            Artist = "Upload Channel",
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.AddRange(track, observation);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var service = new TrackMatchingService(
            dbContext, [], NullLogger<TrackMatchingService>.Instance);
        await service.ProcessObservationAsync(observation.Id, CancellationToken.None);
        await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var credit = await dbContext.TrackArtistCredits.Include(item => item.Artist).SingleAsync();
        Assert.Equal("Canonical Artist", credit.CreditedName);
        Assert.Equal("Canonical Artist", credit.Artist?.Name);
        Assert.Equal(TrackArtistRole.Primary, credit.Role);
        Assert.Single(await dbContext.Artists.ToListAsync());
    }

    [Fact]
    public async Task ProcessObservationAsync_DuplicateClusterAutoMatches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-escape",
            Title = "Kx5 - Escape (feat. Hayla)",
            Artist = "Kx5",
            DurationSeconds = 210,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "mb-escape-1",
                Title = "Escape",
                Artist = "Kx5, Hayla",
                ArtistCredits = ["Kx5", "Hayla"],
                MbidRecording = "mb-escape-1",
                DurationSeconds = 210,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "mb-escape-2",
                Title = "Escape",
                Artist = "Kx5, Hayla",
                ArtistCredits = ["Hayla", "Kx5"],
                MbidRecording = "mb-escape-2",
                DurationSeconds = 215,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "mb-escape-3",
                Title = "Escape",
                Artist = "Kx5",
                MbidRecording = "mb-escape-3",
                DurationSeconds = 240,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.NotNull(result.AcceptedCandidateId);
        Assert.NotNull(result.TrackId);
        Assert.Equal(3, await dbContext.TrackResolutionCandidates.CountAsync());
    }

    [Fact]
    public async Task ProcessObservationAsync_GoodThingsFallApartPrefersExactRecordingOverVsSadSongsVariants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "J9Zjgb03FMQ",
            Title = "Good Things Fall Apart",
            Artist = "ILLENIUM, Jon Bellion",
            DurationSeconds = 218,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "wrong-1",
                Title = "Good Things Fall Apart vs. Sad Songs",
                Artist = "Illenium, Jon Bellion, Said the Sky, Annika Wells",
                MbidRecording = "4e72975f-2f1c-43c7-a72d-4c6711a6bb40",
                DurationSeconds = 366,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "wrong-2",
                Title = "Good Things Fall Apart vs. Sad Songs (mixed)",
                Artist = "ILLENIUM, Jon Bellion, Said the Sky, Annika Wells",
                MbidRecording = "b5fec021-7b4f-4fd1-8e78-cda370672d13",
                DurationSeconds = 358,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "right-1",
                Title = "Good Things Fall Apart",
                Artist = "ILLENIUM & Jon Bellion",
                MbidRecording = "f1fae705-115e-4515-a241-fef12775ac2e",
                Isrc = "USUG11901088",
                DurationSeconds = 220,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "wrong-3",
                Title = "Good Things Fall Apart",
                Artist = "Chance Milic",
                MbidRecording = "8d61c9b4-ae1a-4376-ad1d-6026a6cb2cbf",
                DurationSeconds = 225,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);
        var acceptedCandidate = await dbContext.TrackResolutionCandidates.SingleAsync(candidate => candidate.IsAccepted);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.Equal("f1fae705-115e-4515-a241-fef12775ac2e", acceptedCandidate.MbidRecording);
        Assert.Equal(acceptedCandidate.Id, result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_ExactPlainCandidateBeatsDirtyVariant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "crop-circles-148",
            Title = "Crop Circles",
            Artist = "Jon Bellion",
            DurationSeconds = 148,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "crop-circles-plain",
                Title = "Crop Circles",
                Artist = "Jon Bellion",
                MbidRecording = "crop-circles-plain",
                DurationSeconds = 148,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "crop-circles-dirty",
                Title = "Crop Circles (Dirty)",
                Artist = "Jon Bellion",
                MbidRecording = "crop-circles-dirty",
                DurationSeconds = 148,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var acceptedCandidate = await dbContext.TrackResolutionCandidates.SingleAsync(candidate => candidate.IsAccepted);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.Equal("Crop Circles", acceptedCandidate.Title);
        Assert.Equal(acceptedCandidate.Id, result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_CandidatesOutsideClusterToleranceRemainAmbiguous()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-boundary",
            Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
            Artist = "Lil Nas X",
            DurationSeconds = 211,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "candidate-boundary-1",
                Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
                Artist = "Lil Nas X",
                MbidRecording = "candidate-boundary-1",
                Isrc = "USSM12208809",
                DurationSeconds = 211,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "candidate-boundary-2",
                Title = "STAR WALKIN' (League of Legends Worlds Anthem)",
                Artist = "Lil Nas X",
                MbidRecording = "candidate-boundary-2",
                Isrc = "USSM12208810",
                DurationSeconds = 220,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_UnknownDurationCompetitorsRemainAmbiguous()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "unknown-duration",
            Title = "Crop Circles",
            Artist = "Jon Bellion",
            DurationSeconds = null,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "unknown-duration-1",
                Title = "Crop Circles",
                Artist = "Jon Bellion",
                MbidRecording = "unknown-duration-1",
                DurationSeconds = 148,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "unknown-duration-2",
                Title = "Crop Circles",
                Artist = "Jon Bellion",
                MbidRecording = "unknown-duration-2",
                DurationSeconds = 161,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_AcousticCandidateBeatsPlainVariant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "all-time-low-acoustic",
            Title = "All Time Low (Acoustic)",
            Artist = "Jon Bellion",
            DurationSeconds = 230,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "all-time-low-acoustic-candidate",
                Title = "All Time Low (Acoustic)",
                Artist = "Jon Bellion",
                MbidRecording = "all-time-low-acoustic-candidate",
                DurationSeconds = 224,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "all-time-low-plain-candidate",
                Title = "All Time Low",
                Artist = "Jon Bellion",
                MbidRecording = "all-time-low-plain-candidate",
                DurationSeconds = 230,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var acceptedCandidate = await dbContext.TrackResolutionCandidates.SingleAsync(candidate => candidate.IsAccepted);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.Equal("All Time Low (Acoustic)", acceptedCandidate.Title);
        Assert.Equal(acceptedCandidate.Id, result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_NoiseOnlyMarkersDoNotBlockPlainSongMatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "crop-circles-official-video",
            Title = "Crop Circles (Official Video)",
            Artist = "Jon Bellion",
            DurationSeconds = 148,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "crop-circles-plain-noise-test",
                Title = "Crop Circles",
                Artist = "Jon Bellion",
                MbidRecording = "crop-circles-plain-noise-test",
                DurationSeconds = 148,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.NotNull(result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_DistinctCompetingCandidatesRemainAmbiguous()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-pressure",
            Title = "Under Pressure",
            Artist = "Queen",
            DurationSeconds = 240,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "mb-pressure-1",
                Title = "Under Pressure",
                Artist = "Queen",
                MbidRecording = "mb-pressure-1",
                DurationSeconds = 240,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "mb-pressure-2",
                Title = "Under Pressure",
                Artist = "Queen & David Bowie",
                MbidRecording = "mb-pressure-2",
                DurationSeconds = 242,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_UsesCustomPolicyOverridesWhenProvided()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "custom-policy-threshold",
            Title = "All Time Low (Acoustic)",
            Artist = "Jon Bellion",
            DurationSeconds = 230,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "custom-policy-candidate",
                Title = "All Time Low (Acoustic)",
                Artist = "Jon Bellion",
                MbidRecording = "custom-policy-candidate",
                DurationSeconds = 224,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance,
            Options.Create(new TrackMatchingOptions
            {
                AutoMatchThreshold = 0.99m,
                AmbiguousThreshold = 0.60m,
                AutoMatchMargin = 0.10m,
                MinimumCandidateScore = 0.35m,
                ClusterDurationToleranceSeconds = 5,
                PlaybackModifierMismatchPenalty = -0.20m,
                VersionMismatchPenalty = -0.15m,
                VersionMarkerMatchBonus = 0.05m
            }));

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);

        var storedCandidate = await dbContext.TrackResolutionCandidates.SingleAsync();
        Assert.Contains($"top cluster score {storedCandidate.Score:P0} is below the required 99%", result.ResolutionNotes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"margin {storedCandidate.Score:P0} is below the required 10%", result.ResolutionNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessObservationAsync_RequiresManualReviewWhenEvidenceGateBlocksAnOtherwiseAutomaticMatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "evidence-gate",
            Title = "A Perfect Match",
            Artist = "Source Artist",
            DurationSeconds = 240,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var service = new TrackMatchingService(
            dbContext,
            [new FakeTrackMetadataSearchProvider(new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "evidence-gate-candidate",
                Title = "A Perfect Match",
                Artist = "Different Artist",
                MbidRecording = "evidence-gate-candidate",
                DurationSeconds = 240,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            })],
            NullLogger<TrackMatchingService>.Instance,
            Options.Create(new TrackMatchingOptions
            {
                AutoMatchThreshold = 0.70m,
                AmbiguousThreshold = 0.60m,
                AutoMatchMargin = 0.10m,
                MinimumCandidateScore = 0.35m
            }));

        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);
        Assert.Contains("meets the required 70% score", result.ResolutionNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eligibility requirements are not met", result.ResolutionNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessObservationAsync_PersistsObservationDiagnosticsIntoRawMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "observation-diagnostics",
            Title = "All Time Low (Acoustic)",
            Artist = "Jon Bellion",
            DurationSeconds = 230,
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                SourceType = "youtube",
                ExternalId = "observation-diagnostics",
                Title = "All Time Low (Acoustic)",
                Artist = "Jon Bellion"
            }),
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "observation-diagnostics-candidate",
                Title = "All Time Low (Acoustic)",
                Artist = "Jon Bellion",
                MbidRecording = "observation-diagnostics-candidate",
                DurationSeconds = 224,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "observation-diagnostics-plain",
                Title = "All Time Low",
                Artist = "Jon Bellion",
                MbidRecording = "observation-diagnostics-plain",
                DurationSeconds = 230,
                Explanation = "Suggested by test fixture.",
                RawMetadata = "{}"
            });

        var service = new TrackMatchingService(
            dbContext,
            [provider],
            NullLogger<TrackMatchingService>.Instance);

        await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        var storedObservation = await dbContext.TrackObservations.SingleAsync(item => item.Id == observation.Id);
        var storedMetadata = JsonSerializer.Deserialize<TrackObservationMetadata>(storedObservation.RawMetadata!);

        Assert.NotNull(storedMetadata);
        Assert.NotNull(storedMetadata!.Matching);
        Assert.Contains("acoustic", storedMetadata.Matching!.VersionMarkers);
        Assert.NotNull(storedMetadata.Matching.TopScore);
        Assert.Equal(2, storedMetadata.Matching.DistinctClusterCount);
    }

    [Fact]
    public async Task ProcessObservationAsync_QueueDerivedOfficialVideoPaddingAutoMatchesExactCredits()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "logic-video",
            Title = "1-800-273-8255 ft. Alessia Cara, Khalid (Official Video)",
            Artist = "Logic",
            DurationSeconds = 420,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new FakeTrackMetadataSearchProvider(
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "logic-recording",
                MbidRecording = "logic-recording",
                Title = "1-800-273-8255",
                Artist = "Logic, Alessia Cara, Khalid",
                ArtistCredits = ["Khalid", "Logic", "Alessia Cara"],
                DurationSeconds = 250
            },
            new TrackMatchSearchCandidate
            {
                CandidateSource = "musicbrainz",
                ExternalId = "cover",
                MbidRecording = "cover",
                Title = "1-800-273-8255",
                Artist = "Our Last Night",
                ArtistCredits = ["Our Last Night"],
                DurationSeconds = 235
            });

        var service = new TrackMatchingService(dbContext, [provider], NullLogger<TrackMatchingService>.Instance);
        var result = await service.ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        var accepted = await dbContext.TrackResolutionCandidates.SingleAsync(candidate => candidate.IsAccepted);
        Assert.Equal("logic-recording", accepted.ExternalId);
    }

    [Fact]
    public async Task ProcessObservationAsync_WithoutOfficialVideoMarkerDoesNotIgnoreLargeDurationMismatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "logic-audio",
            Title = "1-800-273-8255 ft. Alessia Cara, Khalid",
            Artist = "Logic",
            DurationSeconds = 420,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();
        var provider = new FakeTrackMetadataSearchProvider(new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz",
            ExternalId = "logic-recording",
            MbidRecording = "logic-recording",
            Title = "1-800-273-8255",
            Artist = "Logic, Alessia Cara, Khalid",
            ArtistCredits = ["Logic", "Alessia Cara", "Khalid"],
            DurationSeconds = 250
        });

        var result = await new TrackMatchingService(dbContext, [provider], NullLogger<TrackMatchingService>.Instance)
            .ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);
    }

    [Fact]
    public async Task ProcessObservationAsync_DecisionIncludesDistinctRunnerUpBeyondFiveDuplicates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "beyond-five",
            Title = "Same Song",
            Artist = "Artist",
            DurationSeconds = 200,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();
        var candidates = Enumerable.Range(1, 5).Select(index => new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz",
            ExternalId = $"duplicate-{index}",
            MbidRecording = "shared-recording",
            Title = "Same Song",
            Artist = "Artist",
            ArtistCredits = ["Artist"],
            DurationSeconds = 200
        }).Append(new TrackMatchSearchCandidate
        {
            CandidateSource = "musicbrainz",
            ExternalId = "distinct-runner-up",
            MbidRecording = "different-recording",
            Title = "Same Song",
            Artist = "Artist feat. Guest",
            ArtistCredits = ["Artist", "Guest"],
            DurationSeconds = 200
        }).ToArray();

        var result = await new TrackMatchingService(dbContext, [new FakeTrackMetadataSearchProvider(candidates)], NullLogger<TrackMatchingService>.Instance)
            .ProcessObservationAsync(observation.Id, CancellationToken.None);

        Assert.Equal(TrackMatchingStatuses.Ambiguous, result.MatchStatus);
        Assert.Null(result.AcceptedCandidateId);
        Assert.Contains(await dbContext.TrackResolutionCandidates.ToListAsync(), candidate => candidate.ExternalId == "distinct-runner-up");
    }

    private sealed class FakeTrackMetadataSearchProvider : ITrackMetadataSearchProvider
    {
        private readonly IReadOnlyList<TrackMatchSearchCandidate> _candidates;

        public FakeTrackMetadataSearchProvider(params TrackMatchSearchCandidate[] candidates)
        {
            _candidates = candidates;
        }

        public Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(TrackObservation observation, CancellationToken cancellationToken)
        {
            return Task.FromResult(_candidates);
        }
    }
}
