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