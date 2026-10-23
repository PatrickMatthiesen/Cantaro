using System.Net;
using System.Text;
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

public sealed class MusicBrainzArtistImportTests
{
    private const string FirstId = "0d5f4b04-3be8-427a-8c24-23b1454b2f31";
    private const string SecondId = "82e3e1de-9243-4e58-b2d2-796c64f76c82";

    [Fact]
    public async Task MusicBrainzSearchToMatching_PreservesAllCreditsAndStableIdentitiesAcrossRepeatedImports()
    {
        using var httpClient = new HttpClient(new RecordingHandler());
        var client = new MusicBrainzQueryClient(httpClient, new MusicBrainzRequestGate(TimeProvider.System));
        var provider = new MusicBrainzSearchProvider(client, NullLogger<MusicBrainzSearchProvider>.Instance,
            Options.Create(new TrackMatchingOptions()), _ => Task.CompletedTask);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var observation = new TrackObservation {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video", Title = "Together",
            Artist = "Alpha feat. Beta", DurationSeconds = 180, MatchStatus = TrackMatchingStatuses.Pending };
        db.TrackObservations.Add(observation);
        await db.SaveChangesAsync();
        var matcher = new TrackMatchingService(db, [provider], NullLogger<TrackMatchingService>.Instance);
        await matcher.ProcessObservationAsync(observation.Id, default);
        Assert.NotNull(observation.TrackId);
        db.ChangeTracker.Clear();
        await matcher.ProcessObservationAsync(observation.Id, default);
        var credits = await db.TrackArtistCredits.Include(c => c.Artist).OrderBy(c => c.Position).ToListAsync();
        Assert.Equal(2, credits.Count);
        Assert.Equal(["Alpha", "Beta"], credits.Select(c => c.CreditedName).ToArray());
        Assert.Equal([FirstId, SecondId], credits.Select(c => c.Artist!.MusicBrainzArtistId!).ToArray());
        Assert.Equal("Alpha, canonical", credits[0].Artist!.Name);
        Assert.Equal("Alpha, sorted", credits[0].Artist!.SortName);
        Assert.Equal(" feat. ", credits[0].JoinPhrase);
        Assert.Equal(2, await db.Artists.CountAsync());
        var links = await db.ArtistProviderLinks.OrderBy(l => l.ExternalId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => {
            Assert.Equal($"https://musicbrainz.org/artist/{link.ExternalId}", link.ProfileUrl);
            Assert.Contains("JoinPhrase", link.RawMetadata);
        });
        Assert.All(credits, credit => Assert.Contains("musicbrainz", credit.Provenance));
        var stored = await db.TrackResolutionCandidates.SingleAsync();
        var metadata = JsonSerializer.Deserialize<TrackMatchCandidateStoredMetadata>(stored.RawMetadata!);
        Assert.Equal(2, metadata!.ArtistIdentities.Count);
        Assert.Empty(await db.ArtistReconciliationConflicts.ToListAsync());
    }

    [Fact]
    public async Task LegacyStoredMusicBrainzCredits_AreAvailableForManualAcceptance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var observation = new TrackObservation {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "legacy-video", Title = "Together",
            Artist = "Alpha feat. Beta", DurationSeconds = 180, MatchStatus = TrackMatchingStatuses.Pending };
        var candidate = new TrackResolutionCandidate {
            Id = Guid.NewGuid(), TrackObservationId = observation.Id, CandidateSource = "musicbrainz", ExternalId = "recording",
            MbidRecording = "recording", Title = "Together", Artist = "Alpha feat. Beta", Score = 0.8m,
            RawMetadata = JsonSerializer.Serialize(new TrackMatchCandidateStoredMetadata {
                ProviderRawMetadata = JsonSerializer.Serialize(new { ArtistCredits = new[] {
                    new { Name = "Alpha", MusicBrainzArtistId = FirstId, JoinPhrase = " feat. ", SortName = "Alpha, sorted" },
                    new { Name = "Beta", MusicBrainzArtistId = SecondId, JoinPhrase = "", SortName = "Beta" } } }) }) };
        observation.Candidates.Add(candidate);
        db.TrackObservations.Add(observation);
        await db.SaveChangesAsync();
        var matcher = new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance);
        await matcher.AcceptCandidateAsync(observation.Id, candidate.Id, default);
        await matcher.AcceptCandidateAsync(observation.Id, candidate.Id, default);
        Assert.Equal(2, await db.Artists.CountAsync());
        Assert.Equal(2, await db.TrackArtistCredits.CountAsync());
        Assert.Equal(2, await db.ArtistProviderLinks.CountAsync());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent($$$"""
                {"created":"2026-10-22T00:00:00Z","count":1,"offset":0,"recordings":[{
                  "id":"846d90b8-61bc-45cc-bb91-e8c2c0088312","score":100,"title":"Together","length":180000,
                  "artist-credit":[
                    {"name":"Alpha","joinphrase":" feat. ","artist":{"id":"{{{FirstId}}}","name":"Alpha, canonical","sort-name":"Alpha, sorted"}},
                    {"name":"Beta","joinphrase":"","artist":{"id":"{{{SecondId}}}","name":"Beta","sort-name":"Beta"}}
                  ]
                }]}
                """, Encoding.UTF8, "application/json") });
    }
}
