using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class SharedTrackMatchingTests
{
    [Fact]
    public void StructuredArtistCreditsCannotBeOverruledByDisplayArtist()
    {
        var source = Observation("Song", "Artist", 160);
        var candidates = new[] { new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "wrong-credits", Title = "Song",
            Artist = "Artist", ArtistCredits = ["Different Artist"], DurationSeconds = 160
        } };
        Assert.Null(TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision.AcceptedCandidate);
        Assert.Null(PlaylistDestinationIdentityResolver.Assess(source, candidates, new()).Accepted);
    }

    [Fact]
    public void EquivalentVersionMarkerOrderDoesNotCreateCompetingFamilies()
    {
        var source = Observation("Song (Live Remix)", "Artist", 160);
        var candidates = new[]
        {
            Candidate("spotify", "one", "Song (Live Remix)", "Artist", 202),
            Candidate("spotify", "two", "Song (Remix Live)", "Artist", 220)
        };
        var canonical = TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision;
        Assert.Equal(TrackMatchingStatuses.Matched, canonical.MatchStatus);
        Assert.Equal(canonical.AcceptedCandidate?.Candidate.ExternalId,
            PlaylistDestinationIdentityResolver.Assess(source, candidates, new()).Accepted?.Candidate.ExternalId);
    }

    [Fact]
    public void EverythingGoesOnAcceptsCatalogRecordingDespiteShorterVideo()
    {
        const string originalTitle = "Everything Goes On - Porter Robinson (Official Music Video) | Star Guardian 2022";
        var source = Observation("Porter Robinson | Star Guardian 2022", "Everything Goes On", 160);
        source.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = originalTitle, OriginalArtist = "League of Legends",
            ChannelTitle = "League of Legends",
            Description = "Everything Goes On\nFeatured artist: Porter Robinson\nVocals performed by: Porter Robinson"
        });
        var candidates = new[]
        {
            Candidate("spotify", "4C4zy9kfjYjr6IcNAdV7ZD", "Everything Goes On", "Porter Robinson, League of Legends", 202, "QZH6S1900628"),
            Candidate("spotify", "karaoke", "Everything Goes On (Karaoke Version)", "karaoke SESH", 203),
            Candidate("spotify", "spanish", "Everything Goes On (Spanish Version)", "MegaMat", 202)
        };

        var canonical = TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision;
        var destination = PlaylistDestinationIdentityResolver.Assess(source, candidates, new());
        Assert.Equal("4C4zy9kfjYjr6IcNAdV7ZD", canonical.AcceptedCandidate?.Candidate.ExternalId);
        Assert.Equal(canonical.AcceptedCandidate?.Candidate.ExternalId, destination.Accepted?.Candidate.ExternalId);
        Assert.Equal(42, canonical.AcceptedCandidate?.DurationDifferenceSeconds);
    }

    [Theory]
    [InlineData("musicbrainz", 202)]
    [InlineData("spotify", 202)]
    [InlineData("youtube", 202)]
    [InlineData("spotify", null)]
    [InlineData("spotify", 1000)]
    public void CanonicalAndPlaylistMatchingAgreeWithoutDurationVeto(string provider, int? duration)
    {
        var source = Observation("Everything Goes On", "Porter Robinson", 160);
        var candidates = new[] { Candidate(provider, "recording", "Everything Goes On", "Porter Robinson", duration) };
        var canonical = TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision;
        var playlist = PlaylistDestinationIdentityResolver.Assess(source, candidates, new());

        Assert.Equal(TrackMatchingStatuses.Matched, canonical.MatchStatus);
        Assert.Equal(canonical.AcceptedCandidate!.Candidate.ExternalId, playlist.Accepted?.Candidate.ExternalId);
        Assert.Equal(1m, canonical.AcceptedCandidate.Score);
        Assert.Equal(160, source.DurationSeconds);
    }

    [Theory]
    [InlineData("musicbrainz")]
    [InlineData("spotify")]
    public void DescriptionCreditsReachBothMatchingPaths(string provider)
    {
        var source = Mortals();
        var candidates = new[]
        {
            Candidate(provider, "normal", "Mortals Funk Remix", "LXNGVX, Warriyo", 146),
            Candidate(provider, "slowed", "Mortals Funk Remix (Slowed)", "LXNGVX, Warriyo", 147),
            Candidate(provider, "cover", "Mortals Funk Remix (Cover)", "LXNGVX, Warriyo", 147),
            Candidate(provider, "missing-credit", "Mortals Funk Remix", "LXNGVX", 147)
        };
        Assert.Equal("normal", TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision.AcceptedCandidate?.Candidate.ExternalId);
        Assert.Equal("normal", PlaylistDestinationIdentityResolver.Assess(source, candidates, new()).Accepted?.Candidate.ExternalId);
        Assert.Equal("MrMoMMusic", source.Artist);
    }

    [Fact]
    public void YouTubeDestinationDescriptionSuppliesArtistsWithoutLosingSourceEvidence()
    {
        var source = Observation("Mortals Funk Remix", "LXNGVX, Warriyo", 146);
        source.SourceType = "spotify";
        var video = Mortals();
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "youtube", ExternalId = "dKmnejGmcjQ", Title = video.Title,
            Artist = video.Artist, ArtistCredits = ["MrMoMMusic"], DurationSeconds = 147,
            RawMetadata = video.RawMetadata
        };
        var accepted = TrackMatchDecisionEngine.Evaluate(source, [candidate], new()).Decision.AcceptedCandidate;
        Assert.NotNull(accepted);
        Assert.Equal("LXNGVX, Warriyo", accepted.Candidate.Artist);
        Assert.Equal("MrMoMMusic", TrackObservationParser.ReadMetadata(video)?.ChannelTitle);
        Assert.Equal(video.RawMetadata, accepted.Candidate.RawMetadata);
    }

    [Fact]
    public void ConflictingDescriptionCreditsCannotWinByScore()
    {
        var source = Mortals("LXNGVX, Warriyo - Mortals Funk Remix\nOther Artist - Mortals Funk Remix");
        var candidates = new[] { Candidate("spotify", "normal", "Mortals Funk Remix", "LXNGVX, Warriyo", 147) };
        Assert.Null(TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision.AcceptedCandidate);
        Assert.Null(PlaylistDestinationIdentityResolver.Assess(source, candidates, new()).Accepted);
    }

    [Theory]
    [InlineData("Everything Goes On (Live)", "Porter Robinson")]
    [InlineData("Everything Goes On (Sped Up)", "Porter Robinson")]
    [InlineData("Everything Goes On", "Other Artist")]
    [InlineData("Something Entirely Different", "Porter Robinson")]
    public void DurationAgreementDoesNotHideIdentityDifferences(string title, string artist)
    {
        var source = Observation("Everything Goes On", "Porter Robinson", 160);
        var candidates = new[] { Candidate("spotify", "wrong", title, artist, 160) };
        Assert.Null(TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision.AcceptedCandidate);
        Assert.Null(PlaylistDestinationIdentityResolver.Assess(source, candidates, new()).Accepted);
    }

    [Fact]
    public void KnownRecordingIdentifierResolvesCompetitionInBothPaths()
    {
        var source = Observation("Song", "Artist", 160);
        source.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { Isrc = "us-abc-24-00002" });
        var candidates = new[]
        {
            Candidate("spotify", "other", "Song", "Artist", 160, "USABC2400001"),
            Candidate("spotify", "known", "Song", "Artist", 220, "USABC2400002")
        };
        Assert.Equal("known", TrackMatchDecisionEngine.Evaluate(source, candidates, new()).Decision.AcceptedCandidate?.Candidate.ExternalId);
        Assert.Equal("known", PlaylistDestinationIdentityResolver.Assess(source, candidates, new()).Accepted?.Candidate.ExternalId);
    }

    private static TrackObservation Mortals(string description = "LXNGVX, Warriyo - Mortals Funk Remix\n\nFollow mommusic")
    {
        var observation = Observation("MORTALS FUNK REMIX // Victory Royale B**ch!", "MrMoMMusic", 147);
        observation.RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
        {
            OriginalTitle = observation.Title, OriginalArtist = observation.Artist,
            ChannelTitle = observation.Artist, Description = description
        });
        return observation;
    }

    private static TrackObservation Observation(string title, string artist, int? duration) => new()
    {
        SourceType = "youtube", ExternalId = "video", Title = title, Artist = artist,
        DurationSeconds = duration, MatchStatus = TrackMatchingStatuses.Pending
    };

    private static TrackMatchSearchCandidate Candidate(string provider, string id, string title, string artist, int? duration, string? isrc = null) => new()
    {
        CandidateSource = provider, ExternalId = id, Title = title, Artist = artist,
        ArtistCredits = artist.Split(", "), DurationSeconds = duration, Isrc = isrc
    };
}
