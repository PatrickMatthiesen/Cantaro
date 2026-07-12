using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
                ArtistMusicBrainzId = "01796d99-3daf-4a4a-a41b-9c1810a3c5b9",
                ArtistSortName = "Kx5",
                MbidRecording = "recording-1",
                DurationSeconds = 241,
                SearchScore = 95,
                RawMetadata = "{}"
            });

        var provider = CreateProvider(fakeClient);
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
        Assert.Equal("01796d99-3daf-4a4a-a41b-9c1810a3c5b9", candidates[0].ArtistMusicBrainzId);
        Assert.Equal("Kx5", candidates[0].ArtistSortName);
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

        var provider = CreateProvider(fakeClient);
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

        var provider = CreateProvider(fakeClient);
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

    [Fact]
    public async Task SearchAsync_UsesPhasedBoundedQueriesInOrder()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        var provider = CreateProvider(fakeClient, new TrackMatchingOptions
        {
            MusicBrainzMaxRequestsPerSearch = 4,
            MusicBrainzCollaboratorVariantLimit = 2
        });

        await provider.SearchAsync(CreateObservation("Good Things Fall Apart", "ILLENIUM, Jon Bellion"), CancellationToken.None);

        Assert.Equal(
        [
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"",
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM & Jon Bellion\"",
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM\"",
            "recording:\"Good Things Fall Apart\""
        ],
        fakeClient.Queries);
    }

    [Fact]
    public async Task SearchAsync_StopsAfterCredibleStrictExactMatch()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        fakeClient.AddResult(
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"",
            new MusicBrainzRecordingMatch
            {
                ExternalId = "recording-good-things",
                Title = "Good Things Fall Apart",
                Artist = "ILLENIUM & Jon Bellion",
                DurationSeconds = 217,
                SearchScore = 1,
                RawMetadata = "{}"
            });
        var provider = CreateProvider(fakeClient);

        var candidates = await provider.SearchAsync(
            CreateObservation("Good Things Fall Apart", "ILLENIUM, Jon Bellion", durationSeconds: 217),
            CancellationToken.None);

        Assert.Single(fakeClient.Queries);
        Assert.Single(candidates);
    }

    [Fact]
    public async Task SearchAsync_DoesNotStopForHighMusicBrainzScoreWithoutLocalCredibility()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        const string strictQuery = "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"";
        const string titleOnlyQuery = "recording:\"Good Things Fall Apart\"";
        fakeClient.AddResult(
            strictQuery,
            new MusicBrainzRecordingMatch
            {
                ExternalId = "wrong-high-score",
                Title = "Good Things Fall Apart vs. Sad Songs",
                Artist = "ILLENIUM, Jon Bellion, Said the Sky",
                DurationSeconds = 366,
                SearchScore = 100,
                RawMetadata = "{}"
            });
        fakeClient.AddResult(
            titleOnlyQuery,
            new MusicBrainzRecordingMatch
            {
                ExternalId = "recording-good-things",
                Title = "Good Things Fall Apart",
                Artist = "ILLENIUM & Jon Bellion",
                DurationSeconds = 217,
                SearchScore = 1,
                RawMetadata = "{}"
            });
        var provider = CreateProvider(fakeClient);

        var candidates = await provider.SearchAsync(
            CreateObservation("Good Things Fall Apart", "ILLENIUM, Jon Bellion", durationSeconds: 217),
            CancellationToken.None);

        Assert.Contains(titleOnlyQuery, fakeClient.Queries);
        Assert.Contains(candidates, candidate => candidate.ExternalId == "recording-good-things");
    }

    [Fact]
    public async Task SearchAsync_DoesNotExceedConfiguredRequestCap()
    {
        var fakeClient = new FakeMusicBrainzQueryClient();
        var provider = CreateProvider(fakeClient, new TrackMatchingOptions
        {
            MusicBrainzMaxRequestsPerSearch = 2,
            MusicBrainzCollaboratorVariantLimit = 4
        });

        await provider.SearchAsync(CreateObservation("Good Things Fall Apart", "ILLENIUM, Jon Bellion"), CancellationToken.None);

        Assert.Equal(2, fakeClient.Queries.Count);
        Assert.Equal(
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM, Jon Bellion\"",
            fakeClient.Queries[0]);
        Assert.Equal(
            "recording:\"Good Things Fall Apart\" AND artist:\"ILLENIUM & Jon Bellion\"",
            fakeClient.Queries[1]);
    }

    private static MusicBrainzSearchProvider CreateProvider(
        FakeMusicBrainzQueryClient fakeClient,
        TrackMatchingOptions? options = null)
    {
        return new MusicBrainzSearchProvider(
            fakeClient,
            NullLogger<MusicBrainzSearchProvider>.Instance,
            Options.Create(options ?? new TrackMatchingOptions()),
            _ => Task.CompletedTask);
    }

    private static TrackObservation CreateObservation(string title, string? artist, int? durationSeconds = null) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = "youtube",
        ExternalId = "video-1",
        Title = title,
        Artist = artist,
        DurationSeconds = durationSeconds,
        MatchStatus = TrackMatchingStatuses.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

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
