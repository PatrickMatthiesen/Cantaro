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
