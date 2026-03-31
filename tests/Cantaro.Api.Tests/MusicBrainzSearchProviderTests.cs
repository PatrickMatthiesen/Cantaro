using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public class MusicBrainzSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_FallsBackToTitleOnlyQuery_WhenTitleAndArtistYieldNoResults()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        fakeClient.AddResult(
            "recording:\"Escape\"",
            new MusicBrainzRecordingMatch
            {
                ExternalId = "recording-1",
                Title = "Escape",
                Artist = "Kx5",
                MbidRecording = "recording-1",
                DurationSeconds = 241,
                SearchScore = 95,
                RawMetadata = "{}"
            });

        var provider = new MusicBrainzSearchProvider(fakeClient, NullLogger<MusicBrainzSearchProvider>.Instance);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "video-1",
            Title = "Kx5 - Escape (feat. Hayla) [Official Lyric Video]",
            Artist = "Kx5",
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.NotEmpty(fakeClient.Queries);
        Assert.Equal("recording:\"Escape\" AND artist:\"Kx5\"", fakeClient.Queries[0]);
        Assert.Contains("recording:\"Escape\"", fakeClient.Queries);
        Assert.Single(candidates);
        Assert.Equal("Escape", candidates[0].Title);
        Assert.Equal("Kx5", candidates[0].Artist);
    }

    [Fact]
    public async Task SearchAsync_QueriesIndividualCollaborationArtists_WhenCombinedArtistClauseMisses()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        fakeClient.AddResult(
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"",
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-1",
                Title = "Good Things Fall Apart vs. Sad Songs",
                Artist = "Illenium, Jon Bellion, Said the Sky, Annika Wells",
                MbidRecording = "wrong-1",
                DurationSeconds = 366,
                SearchScore = 100,
                RawMetadata = "{}"
            });
        fakeClient.AddResult(
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM\"",
            new MusicBrainzRecordingMatch
            {
                ExternalId = "recording-good-things",
                Title = "Good Things Fall Apart",
                Artist = "ILLENIUM & Jon Bellion",
                MbidRecording = "f1fae705-115e-4515-a241-fef12775ac2e",
                DurationSeconds = 217,
                SearchScore = 100,
                RawMetadata = "{}"
            });

        var provider = new MusicBrainzSearchProvider(fakeClient, NullLogger<MusicBrainzSearchProvider>.Instance);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "J9Zjgb03FMQ",
            Title = "Good Things Fall Apart",
            Artist = "ILLENIUM, Jon Bellion",
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.Contains("recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"", fakeClient.Queries);
        Assert.Contains("recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM\"", fakeClient.Queries);
        Assert.Contains(candidates, candidate => candidate.ExternalId == "recording-good-things");
    }

    [Fact]
    public async Task SearchAsync_DoesNotDropLaterMatchesWhenEarlyQueriesAlreadyFilledFiveCandidates()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        fakeClient.AddResult(
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"",
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-1",
                Title = "Good Things Fall Apart vs. Sad Songs",
                Artist = "Illenium, Jon Bellion, Said the Sky, Annika Wells",
                MbidRecording = "wrong-1",
                DurationSeconds = 366,
                SearchScore = 100,
                RawMetadata = "{}"
            },
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-2",
                Title = "Good Things Fall Apart vs. Sad Songs (mixed)",
                Artist = "ILLENIUM, Jon Bellion, Said the Sky, Annika Wells",
                MbidRecording = "wrong-2",
                DurationSeconds = 358,
                SearchScore = 95,
                RawMetadata = "{}"
            });
        fakeClient.AddResult(
            "recording:\"Good Things Fall Apart\"",
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-3",
                Title = "Good Things Fall Apart",
                Artist = "Chance Milic",
                MbidRecording = "wrong-3",
                DurationSeconds = 225,
                SearchScore = 100,
                RawMetadata = "{}"
            },
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-4",
                Title = "Good Things Fall Apart",
                Artist = "Kari Kirkland",
                MbidRecording = "wrong-4",
                DurationSeconds = 322,
                SearchScore = 100,
                RawMetadata = "{}"
            },
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-5",
                Title = "Good Things Fall Apart",
                Artist = "John Louis / Benjamin Mariano",
                MbidRecording = "wrong-5",
                DurationSeconds = 255,
                SearchScore = 100,
                RawMetadata = "{}"
            },
            new MusicBrainzRecordingMatch
            {
                ExternalId = "recording-good-things",
                Title = "Good Things Fall Apart",
                Artist = "ILLENIUM & Jon Bellion",
                MbidRecording = "f1fae705-115e-4515-a241-fef12775ac2e",
                DurationSeconds = 217,
                SearchScore = 100,
                RawMetadata = "{}"
            });

        var provider = new MusicBrainzSearchProvider(fakeClient, NullLogger<MusicBrainzSearchProvider>.Instance);
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = "J9Zjgb03FMQ",
            Title = "Good Things Fall Apart",
            Artist = "ILLENIUM, Jon Bellion",
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var candidates = await provider.SearchAsync(observation, CancellationToken.None);

        Assert.True(candidates.Count > 5);
        Assert.Contains(candidates, candidate => candidate.ExternalId == "recording-good-things");
    }

    private sealed class FakeMusicBrainzQueryClient : IMusicBrainzQueryClient
    {
        private readonly Dictionary<string, IReadOnlyList<MusicBrainzRecordingMatch>> _responses =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> Queries { get; } = [];

        public void AddResult(string query, params MusicBrainzRecordingMatch[] matches)
        {
            _responses[query] = matches;
        }

        public Task<IReadOnlyList<MusicBrainzRecordingMatch>> FindRecordingsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);
            IReadOnlyList<MusicBrainzRecordingMatch> result = _responses.TryGetValue(query, out var matches)
                ? matches.Take(limit).ToList().AsReadOnly()
                : Array.Empty<MusicBrainzRecordingMatch>();

            return Task.FromResult(result);
        }
    }
}