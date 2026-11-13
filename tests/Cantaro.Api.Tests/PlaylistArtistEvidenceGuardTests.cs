using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistArtistEvidenceGuardTests
{
    [Fact]
    public async Task CaptureAsync_ReadsAllEstablishedKeysAndCopiesValuesFromTrackedEntities()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var trackId = Guid.NewGuid();
        await using (var setup = new ApplicationDbContext(options))
        {
            setup.Tracks.Add(new Track {  Id = trackId  });
            setup.ProviderCatalogItems.AddRange(
                new ProviderCatalogItem { Id = Guid.NewGuid(), TrackId = trackId, SourceType = "youtube", ExternalId = "source", FactsJson = "{}" },
                new ProviderCatalogItem { Id = Guid.NewGuid(), TrackId = trackId, SourceType = "spotify", ExternalId = "established", FactsJson = "{}" });
            foreach (var item in setup.ProviderCatalogItems.Local) CatalogFixture.Verify(item, new ProviderCatalogFacts { Title = "Song", Artist = "Artist" });
            await setup.SaveChangesAsync();
        }
        await using var db = new ApplicationDbContext(options);
        var track = await db.Tracks.SingleAsync();
        await db.ProviderCatalogItems.SingleAsync(s => s.SourceType == "youtube");
        Assert.Single(track.ProviderItems);
        Assert.False(db.Entry(track).Collection(t => t.ProviderItems).IsLoaded);

        var evidence = await PlaylistArtistEvidenceGuard.CaptureAsync(db, track, default);
        Assert.True(evidence.CorroboratesRecording(Candidate("established")));
        var mapping = await db.ProviderCatalogItems.SingleAsync(s => s.SourceType == "spotify");
        db.ProviderCatalogItems.Remove(mapping);
        db.ProviderCatalogItems.Add(new ProviderCatalogItem { Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "spotify", ExternalId = "late", FactsJson = "{}" });
        track.Isrc = "USABC2400001";
        track.MbidRecording = "late-recording";
        await db.SaveChangesAsync();

        Assert.True(evidence.CorroboratesRecording(Candidate("established")));
        Assert.False(evidence.CorroboratesRecording(Candidate("late", track.Isrc, track.MbidRecording)));
    }

    [Theory]
    [InlineData("isrc", false, true)]
    [InlineData("musicbrainz", false, true)]
    [InlineData("isrc", true, false)]
    [InlineData("musicbrainz", true, false)]
    public async Task CaptureAsync_NormalizesStableIdsAndRejectsContradictionsDespiteEstablishedMapping(
        string identifier, bool contradictory, bool accepted)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var track = new Track {  Id = Guid.NewGuid(),
            Isrc = identifier == "isrc" ? "us-abc-24-00001" : null,
            MbidRecording = identifier == "musicbrainz" ? " RECORDING-A " : null  };
        db.Tracks.Add(track);
        // Matching stable IDs must work without a provider mapping. A contradictory
        // ID must still reject an otherwise established provider recording key.
        if (contradictory)
            db.ProviderCatalogItems.Add(new ProviderCatalogItem { Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "spotify", ExternalId = "candidate", FactsJson = "{}" });
        await db.SaveChangesAsync();
        var evidence = await PlaylistArtistEvidenceGuard.CaptureAsync(db, track, default);
        var candidate = Candidate("candidate",
            identifier == "isrc" ? contradictory ? "USABC2400002" : "USABC2400001" : null,
            identifier == "musicbrainz" ? contradictory ? "recording-b" : "recording-a" : null);

        // Later tracked scalar changes cannot change the captured decision.
        track.Isrc = candidate.Isrc;
        track.MbidRecording = candidate.MbidRecording;
        Assert.Equal(accepted, evidence.CorroboratesRecording(candidate));
    }

    private static TrackMatchSearchCandidate Candidate(string id, string? isrc = null, string? mbid = null) => new() {
        CandidateSource = "spotify", ExternalId = id, Title = "Song", Artist = "Artist",
        Isrc = isrc, MbidRecording = mbid };
}
