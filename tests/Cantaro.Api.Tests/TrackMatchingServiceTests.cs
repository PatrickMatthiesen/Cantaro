using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
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

        var acceptedCandidate = await dbContext.TrackResolutionCandidates.SingleAsync();

        Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
        Assert.Equal(acceptedCandidate.Id, result.AcceptedCandidateId);
        Assert.True(acceptedCandidate.IsAccepted);
        Assert.NotNull(result.TrackId);
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
