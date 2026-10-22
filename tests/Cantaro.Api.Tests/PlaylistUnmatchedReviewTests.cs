using System.Reflection;
using System.Net;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistUnmatchedReviewTests
{
    [Fact]
    public void AcceptedExpandedCatalogCreditsDoNotShowAConflictWarning()
    {
        var source = new TrackObservation
        {
            SourceType = "youtube", ExternalId = "video", Title = "Mortals Funk Remix",
            Artist = "LXNGVX, Warriyo", DurationSeconds = 147, MatchStatus = TrackMatchingStatuses.Pending
        };
        var candidate = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "normal", Title = "Mortals Funk Remix",
            Artist = "Warriyo, LXNGVX, Laura Brehm", ArtistCredits = ["Warriyo", "LXNGVX", "Laura Brehm"],
            Isrc = "USABC2400001", DurationSeconds = 146
        };
        var options = new TrackMatchingOptions();
        var assessment = PlaylistDestinationIdentityResolver.Assess(source, [candidate], options);
        Assert.NotNull(assessment.Accepted);
        Assert.Null(PlaylistDestinationIdentityResolver.DescribeCandidate(assessment.Accepted, options));
    }

    [Fact]
    public async Task RefreshSourceEvidence_LoadsMissingDescriptionAndMakesItAvailableToSharedMatcher()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var source = scope.Entry.TrackObservation!;
        source.Title = "MORTALS FUNK REMIX // Victory Royale B**ch!";
        source.Artist = "MrMoMMusic";
        source.RawMetadata = "{}";
        await scope.Db.SaveChangesAsync();
        var reads = 0;
        Task<YouTubeVideoMetadataDto?> Read(string videoId, CancellationToken ct)
        {
            reads++;
            Assert.Equal(source.ExternalId, videoId);
            return Task.FromResult<YouTubeVideoMetadataDto?>(new()
            {
                VideoId = videoId, Title = source.Title, ChannelTitle = "MrMoMMusic",
                Description = "LXNGVX, Warriyo - Mortals Funk Remix"
            });
        }
        await scope.Review.RefreshSourceEvidenceAsync(scope.Entry, Read, CancellationToken.None);
        await scope.Review.RefreshSourceEvidenceAsync(scope.Entry, Read, CancellationToken.None);
        Assert.Equal(1, reads);
        var saved = await scope.Db.TrackObservations.AsNoTracking().SingleAsync(item => item.Id == source.Id);
        var hypothesis = TrackObservationParser.ParseSearchHypotheses(saved)[0];
        Assert.Equal("Mortals Funk Remix", hypothesis.SearchTitle);
        Assert.Equal("LXNGVX, Warriyo", hypothesis.SearchArtist);
        Assert.Equal("MrMoMMusic", saved.Artist);
        Assert.Equal(TrackMatchingStatuses.Matched, saved.MatchStatus);
        Assert.Empty(await scope.Db.TrackSourceIds.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshSourceEvidence_UnavailableVideoDoesNotReplaceSavedEvidence(bool wrongVideo)
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var source = scope.Entry.TrackObservation!;
        var before = source.RawMetadata;
        await scope.Review.RefreshSourceEvidenceAsync(scope.Entry, (_, _) =>
            Task.FromResult(wrongVideo ? new YouTubeVideoMetadataDto
            { VideoId = "other-video", Title = "Other", Description = "Wrong credits" } : null), CancellationToken.None);
        Assert.Equal(before, source.RawMetadata);
    }

    [Fact]
    public async Task RefreshSourceEvidence_EmptyDescriptionIsNotRepeatedlyFetched()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var reads = 0;
        Task<YouTubeVideoMetadataDto?> Read(string id, CancellationToken ct)
        {
            reads++;
            return Task.FromResult<YouTubeVideoMetadataDto?>(new() { VideoId = id, Title = "Song" });
        }
        await scope.Review.RefreshSourceEvidenceAsync(scope.Entry, Read, CancellationToken.None);
        await scope.Review.RefreshSourceEvidenceAsync(scope.Entry, Read, CancellationToken.None);
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("youtube")]
    public async Task ListAsync_ReturnsOnlyMissingDestinationIdentitiesForOwner(string service)
    {
        await using var scope = await Scope.CreateAsync(service);
        var second = scope.AddEntry("Other", "video-other");
        scope.Db.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = second.TrackId!.Value,
            SourceType = service, ExternalId = "known-destination"
        });
        await scope.Db.SaveChangesAsync();

        var unmatched = await scope.Review.ListAsync(scope.UserId,
            scope.Playlist.Id, scope.Link.Id, CancellationToken.None);

        var item = Assert.Single(unmatched);
        Assert.Equal(scope.Entry.Id, item.EntryId);
        Assert.Equal("Original source video title", item.Title);
        Assert.Equal(service == "spotify"
            ? "https://www.youtube.com/watch?v=source-video"
            : "https://open.spotify.com/track/source-video", item.SourceUrl);
        Assert.Empty(await scope.Db.TrackSourceIds.Where(source => source.SourceType == service
            && source.ExternalId == "new-destination").ToListAsync());
        await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.ListAsync(
            scope.UserId + 1, scope.Playlist.Id, scope.Link.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmAsync_RejectsTamperedTokenAndConflictingProviderIdentity()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var token = scope.Token(Candidate("new-destination"));
        var middle = token.Length / 2;
        var tampered = token[..middle] + (token[middle] == 'a' ? 'b' : 'a') + token[(middle + 1)..];
        await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.ConfirmAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, tampered, CancellationToken.None));

        var other = new Track { Id = Guid.NewGuid(), SearchTitle = "Other" };
        scope.Db.Tracks.Add(other);
        await scope.Db.SaveChangesAsync();
        scope.Db.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = other.Id,
            SourceType = "spotify", ExternalId = "new-destination"
        });
        await scope.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.ConfirmAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id, token, CancellationToken.None));
        Assert.Equal("recording_already_linked", error.Code);
        Assert.Equal(other.Id, (await scope.Db.TrackSourceIds.SingleAsync()).TrackId);
    }

    [Fact]
    public async Task ConfirmAsync_RejectsExpiredCrossEntryAndChangedEvidence()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var candidate = Candidate("new-destination");
        var expired = scope.Token(candidate, DateTimeOffset.UtcNow.AddMinutes(-11));
        await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.ConfirmAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            expired, CancellationToken.None));

        var original = scope.Token(candidate);
        var another = scope.AddEntry("Another", "another-source");
        await scope.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.ConfirmAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, another.Id,
            original, CancellationToken.None));

        var observation = await scope.Db.TrackObservations.SingleAsync(item =>
            item.Id == scope.Entry.TrackObservationId);
        observation.DurationSeconds = 203;
        observation.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(2);
        await scope.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<PlatformApiException>(() => scope.Review.ConfirmAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, scope.Entry.Id,
            original, CancellationToken.None));
        Assert.Empty(await scope.Db.TrackSourceIds.ToListAsync());
    }

    [Fact]
    public async Task ConfirmAsync_SavesIdentityWithoutWritingProviderPlaylist()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var token = scope.Token(Candidate("new-destination"));

        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id,
            scope.Link.Id, scope.Entry.Id, token, CancellationToken.None);

        var mapping = await scope.Db.TrackSourceIds.SingleAsync();
        Assert.Equal(scope.Entry.TrackId, mapping.TrackId);
        Assert.Equal("new-destination", mapping.ExternalId);
        Assert.Contains("playlist_manual_review", mapping.OriginMetadata);
        Assert.Empty(await scope.Review.ListAsync(scope.UserId,
            scope.Playlist.Id, scope.Link.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmAsync_ReusesExistingRecordingAndMapsOriginalSource()
    {
        await using var scope = await Scope.CreateAsync("spotify");
        var canonicalId = scope.Entry.TrackId!.Value;
        scope.Entry.TrackId = null;
        var observation = await scope.Db.TrackObservations.SingleAsync();
        observation.TrackId = null;
        observation.MatchStatus = TrackMatchingStatuses.Pending;
        scope.Db.TrackSourceIds.Add(new TrackSourceId
        {
            Id = Guid.NewGuid(), TrackId = canonicalId,
            SourceType = "spotify", ExternalId = "new-destination"
        });
        await scope.Db.SaveChangesAsync();
        var token = scope.Token(Candidate("new-destination"));

        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id,
            scope.Link.Id, scope.Entry.Id, token, CancellationToken.None);

        Assert.Equal(canonicalId, observation.TrackId);
        Assert.Equal(canonicalId, (await scope.Db.PlaylistEntries.SingleAsync()).TrackId);
        Assert.Contains(await scope.Db.TrackSourceIds.ToListAsync(), source =>
            source.SourceType == "youtube" && source.ExternalId == "source-video"
            && source.TrackId == canonicalId);
    }

    [Fact]
    public async Task ConfirmAsync_CreatesCanonicalFromSelectedRecordingWithManualOrigin()
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: true);
        scope.Entry.TrackId = null;
        var observation = await scope.Db.TrackObservations.SingleAsync();
        observation.TrackId = null;
        observation.MatchStatus = TrackMatchingStatuses.Pending;
        await scope.Db.SaveChangesAsync();
        var token = scope.Token(Candidate("new-destination"));

        await scope.Review.ConfirmAsync(scope.UserId, scope.Playlist.Id,
            scope.Link.Id, scope.Entry.Id, token, CancellationToken.None);

        Assert.Equal("Confirmed recording during playlist review.", observation.ResolutionNotes);
        Assert.NotNull(observation.TrackId);
        Assert.Equal(observation.TrackId, (await scope.Db.PlaylistEntries.SingleAsync()).TrackId);
        var sources = await scope.Db.TrackSourceIds.ToListAsync();
        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, source => source.SourceType == "youtube" && source.ExternalId == "source-video");
        Assert.Contains(sources, source => source.SourceType == "spotify" && source.ExternalId == "new-destination");
        var created = await scope.Db.Tracks.SingleAsync(track => track.Id == observation.TrackId);
        Assert.Contains("playlist-manual-review", created.VersionEvidence);
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("provider-reuse")]
    [InlineData("isrc-reuse")]
    [InlineData("new")]
    public async Task CustomSpotifySearchAndConfirmation_PreserveOrderedIdentitiesAndRepeatSafely(string path)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: true);
        using var http = new HttpClient(new ReviewSpotifyHandler(collaboration: true));
        var review = await scope.WithSpotifyAsync(http);
        var originalTrackId = scope.Entry.TrackId!.Value;
        await PrepareManualPathAsync(scope, path);
        // A same-name artist elsewhere in the library must not absorb this identity.
        var other = new Artist { Id = Guid.NewGuid(), Name = "Porter Robinson" };
        scope.Db.Artists.Add(other);
        scope.Db.ArtistProviderLinks.Add(new ArtistProviderLink
        {
            Id = Guid.NewGuid(), Artist = other, ArtistId = other.Id,
            Provider = "spotify", ExternalId = "unrelated-artist"
        });
        await scope.Db.SaveChangesAsync();

        await SearchAndConfirmAsync(scope, review, scope.Entry);

        var trackId = (await scope.Db.PlaylistEntries.SingleAsync()).TrackId!.Value;
        if (path == "new") Assert.NotEqual(originalTrackId, trackId);
        else Assert.Equal(originalTrackId, trackId);
        scope.Db.ChangeTracker.Clear();
        var credits = await scope.Db.TrackArtistCredits.AsNoTracking()
            .Where(c => c.TrackId == trackId).OrderBy(c => c.Position).ToArrayAsync();
        Assert.Equal(["Porter Robinson", "Guest"], credits.Select(c => c.CreditedName));
        Assert.Equal([0, 1], credits.Select(c => c.Position));
        Assert.All(credits, c => Assert.NotEqual(other.Id, c.ArtistId));
        var artistIds = new[] { "artist-one", "artist-two" };
        for (var index = 0; index < credits.Length; index++)
        {
            var link = await scope.Db.ArtistProviderLinks.SingleAsync(l => l.ArtistId == credits[index].ArtistId);
            Assert.Equal("spotify", link.Provider);
            Assert.Equal(artistIds[index], link.ExternalId);
            Assert.Equal($"https://open.spotify.com/artist/{artistIds[index]}", link.ProfileUrl);
            Assert.Equal($"https://images.example/{artistIds[index]}.jpg", link.ArtworkUrl);
            Assert.Contains(artistIds[index], link.RawMetadata);
            var provenance = JsonSerializer.Deserialize<Dictionary<string, ArtistCreditMetadata>>(credits[index].Provenance!)!;
            Assert.Equal(link.ExternalId, provenance["spotify"].ExternalId);
            Assert.Equal(link.ProfileUrl, provenance["spotify"].ProfileUrl);
        }
        Assert.Empty(await scope.Db.ArtistReconciliationConflicts.ToArrayAsync());
        var trackCount = await scope.Db.Tracks.CountAsync();

        // A later unresolved entry can confirm the same recording without duplicating credits or identities.
        var repeated = AddUnresolvedReviewEntry(scope);
        await scope.Db.SaveChangesAsync();
        await SearchAndConfirmAsync(scope, review, repeated);
        Assert.Equal(trackId, (await scope.Db.TrackObservations.SingleAsync(o => o.Id == repeated.TrackObservationId)).TrackId);
        Assert.Equal(trackId, (await scope.Db.PlaylistEntries.SingleAsync()).TrackId);
        Assert.Equal(trackCount, await scope.Db.Tracks.CountAsync());
        Assert.Equal(3, await scope.Db.Artists.CountAsync());
        Assert.Equal(3, await scope.Db.ArtistProviderLinks.CountAsync());
        Assert.Equal(2, await scope.Db.TrackArtistCredits.CountAsync());
        Assert.Empty(await scope.Db.ArtistReconciliationConflicts.ToArrayAsync());
    }

    [Theory]
    [InlineData("existing")]
    [InlineData("provider-reuse")]
    [InlineData("isrc-reuse")]
    public async Task CustomSpotifyConfirmation_KeepsConflictingStableIdentityReviewableAndDeduplicated(string path)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: true);
        using var http = new HttpClient(new ReviewSpotifyHandler(collaboration: false));
        var review = await scope.WithSpotifyAsync(http);
        var trackId = scope.Entry.TrackId!.Value;
        await PrepareManualPathAsync(scope, path);
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Porter Robinson" };
        scope.Db.Artists.Add(artist);
        scope.Db.ArtistProviderLinks.Add(new ArtistProviderLink
        {
            Id = Guid.NewGuid(), Artist = artist, ArtistId = artist.Id,
            Provider = "spotify", ExternalId = "different-stable-artist"
        });
        scope.Db.TrackArtistCredits.Add(new TrackArtistCredit
        {
            Id = Guid.NewGuid(), TrackId = trackId, Artist = artist, ArtistId = artist.Id,
            CreditedName = artist.Name, Position = 0, Role = TrackArtistRole.Primary
        });
        await scope.Db.SaveChangesAsync();

        await SearchAndConfirmAsync(scope, review, scope.Entry);
        var conflict = await scope.Db.ArtistReconciliationConflicts.AsNoTracking().SingleAsync();
        Assert.Equal(trackId, conflict.TrackId);
        Assert.Contains("artist-one", conflict.Evidence);
        Assert.Contains("https://open.spotify.com/artist/artist-one", conflict.Evidence);

        var repeated = AddUnresolvedReviewEntry(scope);
        await scope.Db.SaveChangesAsync();
        await SearchAndConfirmAsync(scope, review, repeated);
        scope.Db.ChangeTracker.Clear();
        Assert.Equal(conflict.Id, (await scope.Db.ArtistReconciliationConflicts.SingleAsync()).Id);
        Assert.Equal(artist.Id, (await scope.Db.TrackArtistCredits.SingleAsync()).ArtistId);
        Assert.Equal("different-stable-artist", (await scope.Db.ArtistProviderLinks.SingleAsync()).ExternalId);
        Assert.Single(await scope.Db.Artists.ToArrayAsync());
        Assert.Contains(await scope.Db.TrackSourceIds.ToArrayAsync(), s =>
            s.SourceType == "spotify" && s.ExternalId == "new-destination" && s.TrackId == trackId);
    }

    private static async Task PrepareManualPathAsync(Scope scope, string path)
    {
        if (path != "existing")
        {
            scope.Entry.TrackId = null;
            scope.Entry.Track = null;
            scope.Entry.TrackObservation!.TrackId = null;
            scope.Entry.TrackObservation.Track = null;
            scope.Entry.TrackObservation.MatchStatus = TrackMatchingStatuses.Pending;
        }
        var track = await scope.Db.Tracks.SingleAsync();
        if (path == "provider-reuse")
            scope.Db.TrackSourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = track.Id, SourceType = "spotify", ExternalId = "new-destination"
            });
        if (path == "isrc-reuse") track.Isrc = "USABC2400001";
    }

    private static PlaylistEntry AddUnresolvedReviewEntry(Scope scope)
    {
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "second-source-video",
            Title = "Everything Goes On", Artist = "Porter Robinson", DurationSeconds = 160,
            MatchStatus = TrackMatchingStatuses.Pending,
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { Description = "" })
        };
        var entry = new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id, TrackObservation = observation,
            TrackObservationId = observation.Id, Position = 1
        };
        scope.Db.PlaylistEntries.Add(entry);
        return entry;
    }

    private static async Task SearchAndConfirmAsync(Scope scope, PlaylistUnmatchedReviewService review, PlaylistEntry entry)
    {
        var search = await review.SearchAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            entry.Id, "Everything Goes On Porter Robinson", CancellationToken.None);
        var choice = Assert.Single(search.Candidates);
        await review.ConfirmAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id,
            entry.Id, choice.CandidateToken, CancellationToken.None);
        // A consumed choice cannot create additional mappings or evidence.
        var replay = await Assert.ThrowsAsync<PlatformApiException>(() => review.ConfirmAsync(
            scope.UserId, scope.Playlist.Id, scope.Link.Id, entry.Id, choice.CandidateToken, CancellationToken.None));
        Assert.Equal("unmatched_entry_not_found", replay.Code);
    }

    private sealed class ReviewSpotifyHandler(bool collaboration) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            Assert.Contains(path, new[] { "/v1/me", "/v1/search", "/v1/artists/artist-one", "/v1/artists/artist-two" });
            var body = path == "/v1/me" ? "{\"id\":\"account\"}"
                : path.StartsWith("/v1/artists/", StringComparison.Ordinal) ? JsonSerializer.Serialize(new
                {
                    id = path.Split('/').Last(),
                    name = path.EndsWith("artist-one", StringComparison.Ordinal) ? "Porter Robinson" : "Guest",
                    images = new[] { new { url = $"https://images.example/{path.Split('/').Last()}.jpg" } }
                })
                : JsonSerializer.Serialize(new
            {
                tracks = new { items = new[] { new
                {
                    id = "new-destination", type = "track", name = "Everything Goes On", duration_ms = 160000,
                    external_ids = new { isrc = "USABC2400001" },
                    artists = (collaboration ? new[] { "Porter Robinson", "Guest" } : ["Porter Robinson"])
                        .Select((name, index) => new
                        {
                            id = index == 0 ? "artist-one" : "artist-two", name,
                            external_urls = new { spotify = $"https://open.spotify.com/artist/artist-{(index == 0 ? "one" : "two")}" }
                        })
                } } }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public void Assessment_AcceptsDurationDifferenceButReportsVersionConflict()
    {
        var evidence = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video",
            Title = "Everything Goes On", Artist = "Porter Robinson",
            DurationSeconds = 160, MatchStatus = TrackMatchingStatuses.Pending
        };
        var duration = PlaylistDestinationIdentityResolver.Assess(evidence,
            [Candidate("spotify", duration: 202)], new TrackMatchingOptions());
        Assert.NotNull(duration.Accepted);
        Assert.Equal(42, duration.Accepted.DurationDifferenceSeconds);
        var empty = PlaylistDestinationIdentityResolver.Assess(evidence, [], new TrackMatchingOptions());
        Assert.Contains("No results", empty.Reason);
        var version = PlaylistDestinationIdentityResolver.Assess(evidence,
            [Candidate("spotify", "Everything Goes On (Live)", 160)], new TrackMatchingOptions());
        Assert.Contains("different version", version.Reason);
    }

    [Fact]
    public void ReviewCandidates_CollapseEquivalentCatalogReleasesButKeepDifferentVersions()
    {
        var evidence = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video",
            Title = "Song", Artist = "Artist", DurationSeconds = 180,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var options = new TrackMatchingOptions();
        var candidates = new[]
        {
            new TrackMatchSearchCandidate { CandidateSource = "spotify", ExternalId = "album",
                Title = "Song", Artist = "Artist", ArtistCredits = ["Artist"],
                DurationSeconds = 180, Isrc = "USABC2400001" },
            new TrackMatchSearchCandidate { CandidateSource = "spotify", ExternalId = "single",
                Title = "Song", Artist = "Artist", ArtistCredits = ["Artist"],
                DurationSeconds = 181, Isrc = "USABC2400001" },
            new TrackMatchSearchCandidate { CandidateSource = "spotify", ExternalId = "live",
                Title = "Song (Live)", Artist = "Artist", ArtistCredits = ["Artist"],
                DurationSeconds = 181, Isrc = "USABC2400001" }
        };
        var assessment = PlaylistDestinationIdentityResolver.Assess(evidence, candidates, options);

        var review = PlaylistDestinationIdentityResolver.DistinctReviewCandidates(assessment.Ranked, options);

        Assert.Equal(2, review.Count);
        Assert.Contains(review, candidate => candidate.Candidate.ExternalId == "live");
    }

    [Fact]
    public void Assessment_NamesMissingFeaturedArtistInCreditConflict()
    {
        var source = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video",
            Title = "Together Forever ft. Rythm", Artist = "Exyl",
            DurationSeconds = 195, MatchStatus = TrackMatchingStatuses.Pending
        };
        var result = new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = "spotify-track",
            Title = "Together Forever", Artist = "Exyl", ArtistCredits = ["Exyl"],
            DurationSeconds = 195
        };

        var assessment = PlaylistDestinationIdentityResolver.Assess(source,
            [result], new TrackMatchingOptions());

        Assert.Equal("Source credits Exyl, Rythm; this result credits Exyl.", assessment.Reason);
    }

    private static TrackMatchSearchCandidate Candidate(string externalId,
        string title = "Everything Goes On", int duration = 160) => new()
    {
        CandidateSource = "spotify", ExternalId = externalId,
        Title = title, Artist = "Porter Robinson", ArtistCredits = ["Porter Robinson"],
        DurationSeconds = duration
    };

    private sealed class Scope : IAsyncDisposable
    {
        private readonly IDataProtectionProvider _protector = new EphemeralDataProtectionProvider();
        private readonly SqliteConnection? _connection;
        private Scope(ApplicationDbContext db, Playlist playlist,
            ServicePlaylistMapping link, PlaylistEntry entry, int userId, SqliteConnection? connection)
        {
            Db = db; Playlist = playlist; Link = link; Entry = entry; UserId = userId;
            _connection = connection;
            Review = new PlaylistUnmatchedReviewService(db, null!, null!, [], null!,
                new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance),
                new PlaylistCanonicalReconciliationService(db),
                Options.Create(new TrackMatchingOptions()), _protector);
        }

        public ApplicationDbContext Db { get; }
        public Playlist Playlist { get; }
        public ServicePlaylistMapping Link { get; }
        public PlaylistEntry Entry { get; }
        public int UserId { get; }
        public PlaylistUnmatchedReviewService Review { get; }

        public async Task<PlaylistUnmatchedReviewService> WithSpotifyAsync(HttpClient http)
        {
            http.BaseAddress = new Uri("https://api.spotify.com");
            var encryption = new TokenEncryptionService(_protector);
            var account = await Db.ConnectedServiceAccounts.SingleAsync();
            account.EncryptedAccessToken = encryption.Encrypt("test-access-token");
            account.TokenExpiresAt = DateTime.UtcNow.AddHours(1);
            var metadata = TrackObservationParser.ReadMetadata(Entry.TrackObservation!)!;
            metadata.Description = "";
            Entry.TrackObservation!.RawMetadata = JsonSerializer.Serialize(metadata);
            await Db.SaveChangesAsync();
            var api = new SpotifyApiClient(http, Options.Create(new SpotifyOptions()), new SpotifyRetryDelay());
            var tokens = new SpotifyTokenManager(Db, api, encryption, TimeProvider.System, null!);
            return new PlaylistUnmatchedReviewService(Db, api, tokens, [], null!,
                new TrackMatchingService(Db, [], NullLogger<TrackMatchingService>.Instance),
                new PlaylistCanonicalReconciliationService(Db), Options.Create(new TrackMatchingOptions()), _protector);
        }

        public static async Task<Scope> CreateAsync(string service, bool relational = false)
        {
            const int userId = 5801;
            SqliteConnection? connection = null;
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            if (relational)
            {
                connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                builder.UseSqlite(connection);
            }
            else builder.UseInMemoryDatabase(Guid.NewGuid().ToString());
            var db = new ApplicationDbContext(builder.Options);
            if (relational) await db.Database.EnsureCreatedAsync();
            var now = DateTimeOffset.UtcNow;
            var playlist = new Playlist
            {
                Id = Guid.NewGuid(), UserId = userId, Name = "Løb",
                CreatedAt = now, UpdatedAt = now
            };
            var account = new ConnectedServiceAccount
            {
                Id = 51, UserId = userId, Service = service,
                ExternalAccountId = "account", ConnectionState = "connected"
            };
            var link = new ServicePlaylistMapping
            {
                Id = Guid.NewGuid(), PlaylistId = playlist.Id, UserId = userId,
                Service = service, ServicePlaylistId = "playlist",
                ConnectedServiceAccountId = account.Id,
                ExternalAccountId = account.ExternalAccountId,
                SyncMode = "bidirectional", State = "active"
            };
            var track = new Track
            {
                Id = Guid.NewGuid(), SearchTitle = "Everything Goes On",
                SearchArtist = "Porter Robinson",
                CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
                { Title = "Everything Goes On", Artist = "Porter Robinson", DurationSeconds = 160 })
            };
            var observation = new TrackObservation
            {
                Id = Guid.NewGuid(), SourceType = service == "youtube" ? "spotify" : "youtube",
                ExternalId = "source-video",
                Title = "Everything Goes On", Artist = "Porter Robinson", DurationSeconds = 160,
                MatchStatus = TrackMatchingStatuses.Matched, TrackId = track.Id,
                RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata
                { OriginalTitle = "Original source video title", OriginalArtist = "Porter Robinson" }),
                UpdatedAt = now
            };
            var entry = new PlaylistEntry
            {
                Id = Guid.NewGuid(), PlaylistId = playlist.Id, TrackId = track.Id,
                TrackObservationId = observation.Id, Position = 0, AddedAt = now
            };
            var user = TestUserFactory.Create(userId, "playlist-review@example.com");
            db.AddRange(user, playlist, account, link, track, observation, entry);
            await db.SaveChangesAsync();
            return new Scope(db, playlist, link, entry, userId, connection);
        }

        public PlaylistEntry AddEntry(string name, string sourceId)
        {
            var track = new Track { Id = Guid.NewGuid(), SearchTitle = name };
            var observation = new TrackObservation
            {
                Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = sourceId,
                Title = name, TrackId = track.Id, MatchStatus = TrackMatchingStatuses.Matched
            };
            var entry = new PlaylistEntry
            {
                Id = Guid.NewGuid(), PlaylistId = Playlist.Id, TrackId = track.Id,
                TrackObservationId = observation.Id, Position = 1
            };
            Db.AddRange(track, observation, entry);
            return entry;
        }

        public string Token(TrackMatchSearchCandidate candidate, DateTimeOffset? issuedAt = null)
        {
            var entry = Db.PlaylistEntries.Include(item => item.TrackObservation)
                .Include(item => item.Track).Single(item => item.Id == Entry.Id);
            var fingerprint = (string)typeof(PlaylistUnmatchedReviewService)
                .GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [entry])!;
            var proof = new
            {
                UserId, PlaylistId = Playlist.Id, MappingId = Link.Id,
                EntryId = Entry.Id, ConnectedServiceAccountId = Link.ConnectedServiceAccountId,
                ExternalAccountId = Link.ExternalAccountId, Service = Link.Service,
                Fingerprint = fingerprint, Candidate = candidate,
                IssuedAt = issuedAt ?? DateTimeOffset.UtcNow
            };
            return _protector.CreateProtector("Cantaro.PlaylistSync.MatchReview.v1")
                .Protect(JsonSerializer.Serialize(proof));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
    }
}
