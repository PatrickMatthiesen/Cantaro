using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ZoltraakMatchingRegressionTests
{
    [Fact]
    public void SharedMatcherUsesCoverArtistAndFullDashTitleForZoltraak()
    {
        const string originalTitle = "Zoltraak | Epic Orchestral Cover - Frieren: Beyond Journey's End 葬送のフリーレン";
        var source = new TrackObservation
        {
            SourceType = "youtube",
            ExternalId = "zoltraak-video",
            Title = originalTitle,
            Artist = "Luke Chu",
            DurationSeconds = 137,
            MatchStatus = TrackMatchingStatuses.Pending,
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
            {
                OriginalTitle = originalTitle,
                OriginalArtist = "Luke Chu",
                SearchArtist = "Luke Chu",
                Description = "Composer: Evan Call\n\nCover Artist: Luke Chu"
            })
        };
        var candidates = new[]
        {
            Candidate("correct", "Zoltraak - Epic Orchestral Cover", "Luke Chu"),
            Candidate("composer", "Zoltraak - Epic Orchestral Cover", "Evan Call"),
            Candidate("wrong-cover", "Zoltraak - Epic Orchestral Cover", "Logan Feece")
        };

        var decision = TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision;

        Assert.Equal("correct", decision.AcceptedCandidate?.Candidate.ExternalId);
    }

    private static TrackMatchSearchCandidate Candidate(string id, string title, string artist) => new()
    {
        CandidateSource = "spotify",
        ExternalId = id,
        Title = title,
        Artist = artist,
        ArtistCredits = [artist],
        DurationSeconds = 137,
        Isrc = $"USABC240000{id switch { "correct" => "1", "composer" => "2", _ => "3" }}"
    };
}
