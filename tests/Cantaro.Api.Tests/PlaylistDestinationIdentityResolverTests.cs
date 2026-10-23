using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class PlaylistDestinationIdentityResolverTests
{
    [Fact]
    public async Task ResolveAsync_ConcurrentPlaylistsSearchSharedObservationOnlyOnce()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Guid firstEntryId;
        Guid secondEntryId;
        await using (var setup = new ApplicationDbContext(options))
        {
            var first = await SeedUnresolvedAsync(setup);
            firstEntryId = first.Id;
            var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 99, Name = "Another listener" };
            var second = new PlaylistEntry { Id = Guid.NewGuid(), Playlist = playlist,
                PlaylistId = playlist.Id, TrackObservationId = first.TrackObservationId };
            setup.PlaylistEntries.Add(second);
            await setup.SaveChangesAsync();
            secondEntryId = second.Id;
        }
        var searching = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searches = 0;

        async Task<string?> Resolve(Guid entryId, int userId)
        {
            await using var db = new ApplicationDbContext(options);
            var entry = await db.PlaylistEntries.SingleAsync(item => item.Id == entryId);
            var reconciler = new PlaylistCanonicalReconciliationService(db);
            return await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
                async (observationId, candidate, ct) =>
                {
                    var track = new Track { Id = Guid.NewGuid(), SearchTitle = candidate.Title,
                        SearchArtist = candidate.Artist };
                    db.Tracks.Add(track);
                    var observation = await db.TrackObservations.SingleAsync(item => item.Id == observationId, ct);
                    observation.TrackId = track.Id;
                    observation.MatchStatus = TrackMatchingStatuses.Matched;
                    db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = track.Id,
                        SourceType = observation.SourceType, ExternalId = observation.ExternalId });
                    await reconciler.ReconcileObservationAsync(observationId, track.Id, ct);
                    await db.SaveChangesAsync(ct);
                    return track.Id;
                },
                (observation, track, ct) => reconciler.ReconcileObservationAsync(observation, track, ct),
                new PlatformAccountContext(userId, 7), "spotify", entry,
                async (_, ct) =>
                {
                    Interlocked.Increment(ref searches);
                    searching.TrySetResult();
                    await releaseSearch.Task.WaitAsync(ct);
                    return [Candidate("shared-match", "spotify")];
                }, CancellationToken.None);
        }

        var firstRun = Resolve(firstEntryId, 42);
        await searching.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondRun = Resolve(secondEntryId, 99);
        releaseSearch.SetResult();
        Assert.Equal(new[] { "shared-match", "shared-match" }, await Task.WhenAll(firstRun, secondRun));
        Assert.Equal(1, searches);
        await using var verify = new ApplicationDbContext(options);
        Assert.Single(await verify.Tracks.ToListAsync());
        Assert.Equal(2, await verify.TrackSourceIds.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_AnotherUsersPlaylistReusesSharedIdentityWithoutSearching(bool observationAlreadyResolved)
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName).Options;
        Guid entryId;
        Guid trackId;
        await using (var setup = new ApplicationDbContext(options))
        {
            var original = await SeedCanonicalAsync(setup);
            trackId = original.TrackId!.Value;
            setup.TrackSourceIds.AddRange(
                new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId, SourceType = "youtube", ExternalId = "video1" },
                new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId, SourceType = "spotify", ExternalId = "already-found" });
            if (!observationAlreadyResolved) original.TrackObservation!.TrackId = null;
            var otherPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = 99, Name = "Other user's playlist" };
            var otherEntry = new PlaylistEntry { Id = Guid.NewGuid(), Playlist = otherPlaylist,
                PlaylistId = otherPlaylist.Id, TrackObservationId = original.TrackObservationId };
            setup.PlaylistEntries.Add(otherEntry);
            await setup.SaveChangesAsync();
            entryId = otherEntry.Id;
        }

        // A fresh DbContext models a restarted worker, not an in-memory result cache.
        await using var db = new ApplicationDbContext(options);
        var entry = await db.PlaylistEntries.SingleAsync(item => item.Id == entryId);
        var reconciler = new PlaylistCanonicalReconciliationService(db);
        var result = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("Must not create another track."),
            (observation, track, ct) => reconciler.ReconcileObservationAsync(observation, track, ct),
            new PlatformAccountContext(99, 8), "spotify", entry,
            (_, _) => throw new InvalidOperationException("Must not search a known provider identity."), CancellationToken.None);

        Assert.Equal("already-found", result);
        Assert.Equal(trackId, (await db.PlaylistEntries.AsNoTracking().SingleAsync(item => item.Id == entryId)).TrackId);
        Assert.Single(await db.Tracks.ToListAsync());
        Assert.Equal(2, await db.TrackSourceIds.CountAsync());
    }

    [Fact]
    public async Task ResolveAsync_DoesNotExportMappingClaimedByAnotherTrackDuringMaterialization()
    {
        await using var db = CreateDb();
        var entry = await SeedUnresolvedAsync(db);
        var resolvedTrackId = Guid.NewGuid();
        var otherTrackId = Guid.NewGuid();

        var result = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            async (_, _, ct) =>
            {
                db.Tracks.AddRange(new Track { Id = resolvedTrackId }, new Track { Id = otherTrackId });
                db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = otherTrackId,
                    SourceType = "spotify", ExternalId = "claimed-match" });
                await db.SaveChangesAsync(ct);
                return resolvedTrackId;
            },
            (_, _, _) => throw new InvalidOperationException("Must not reconcile a conflicting identity."),
            new PlatformAccountContext(42, 7), "spotify", entry,
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([Candidate("claimed-match", "spotify")]),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(otherTrackId, (await db.TrackSourceIds.SingleAsync()).TrackId);
    }

    [Fact]
    public async Task ResolveAsync_ExistingCanonicalMetadataIsReusedForNewSourceObservation()
    {
        await using var db = CreateDb();
        var known = await SeedCanonicalAsync(db);
        known.Track!.SearchTitle = "Song";
        known.Track.SearchArtist = "Artist";
        db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = known.TrackId!.Value,
            SourceType = "spotify", ExternalId = "existing-song" });
        var source = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "another-video",
            Title = "Song", Artist = "Artist", DurationSeconds = 180, MatchStatus = TrackMatchingStatuses.Pending };
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 42, Name = "Another playlist" };
        var entry = new PlaylistEntry { Id = Guid.NewGuid(), Playlist = playlist, PlaylistId = playlist.Id,
            TrackObservation = source, TrackObservationId = source.Id };
        db.PlaylistEntries.Add(entry);
        await db.SaveChangesAsync();
        var reconciler = new PlaylistCanonicalReconciliationService(db);

        var result = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("Must reuse the canonical song."),
            (observation, track, ct) => reconciler.ReconcileObservationAsync(observation, track, ct),
            new PlatformAccountContext(42, 7), "spotify", entry,
            (_, _) => throw new InvalidOperationException("Stored evidence is sufficient."), CancellationToken.None);

        Assert.Equal("existing-song", result);
        Assert.Equal(known.TrackId, source.TrackId);
        Assert.Contains(await db.TrackSourceIds.ToListAsync(), item => item.SourceType == "youtube"
            && item.ExternalId == "another-video" && item.TrackId == known.TrackId);
    }

    [Fact]
    public async Task ResolveAsync_UnmatchedSourceObservationKeepsItsExactProviderId()
    {
        await using var db = CreateDb();
        var entry = await SeedUnresolvedAsync(db);
        var searches = 0;

        var id = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("Must not create a canonical track."),
            (_, _, _) => throw new InvalidOperationException("Must not reconcile."),
            new PlatformAccountContext(42, 7), "youtube", entry,
            (_, _) => { searches++; return Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([]); },
            CancellationToken.None);

        Assert.Equal("video1", id);
        Assert.Equal(0, searches);
        Assert.Null((await db.PlaylistEntries.SingleAsync()).TrackId);
    }

    [Fact]
    public async Task ResolveAsync_StrongCrossProviderMatchCreatesCanonicalAndSourceMapping()
    {
        await using var db = CreateDb();
        var entry = await SeedUnresolvedAsync(db);
        var createdTrackId = Guid.NewGuid();

        var id = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            async (observationId, candidate, ct) =>
            {
                Assert.Equal("spotify1", candidate.ExternalId);
                Assert.Equal("Song", candidate.Title);
                db.Tracks.Add(new Track { Id = createdTrackId,
                    CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
                    { Title = "Song", Artist = "Artist", DurationSeconds = 180 }) });
                (await db.TrackObservations.SingleAsync(item => item.Id == observationId, ct)).TrackId = createdTrackId;
                (await db.PlaylistEntries.SingleAsync(item => item.Id == entry.Id, ct)).TrackId = createdTrackId;
                await db.SaveChangesAsync(ct);
                return createdTrackId;
            },
            (_, _, _) => throw new InvalidOperationException("Must not reconcile an unknown destination."),
            new PlatformAccountContext(42, 7), "spotify", entry,
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>(
            [
                new TrackMatchSearchCandidate
                {
                    CandidateSource = "spotify", ExternalId = "spotify1", Title = "Song",
                    Artist = "Artist", ArtistCredits = ["Artist"], DurationSeconds = 180
                }
            ]), CancellationToken.None);

        Assert.Equal("spotify1", id);
        var source = await db.TrackSourceIds.SingleAsync();
        Assert.Equal(createdTrackId, source.TrackId);
        Assert.Equal("spotify", source.SourceType);
        Assert.True(source.Confidence >= 0.85m);
    }

    [Fact]
    public async Task ResolveAsync_DistinctRecordingIdentifiersAreNotSavedWithoutDisambiguation()
    {
        await using var db = CreateDb();
        var entry = await SeedUnresolvedAsync(db);

        var id = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("Must not materialize a weak match."),
            (_, _, _) => throw new InvalidOperationException("Must not reconcile."),
            new PlatformAccountContext(42, 7), "spotify", entry,
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>(
            [
                new TrackMatchSearchCandidate { CandidateSource = "spotify", ExternalId = "one",
                    Title = "Song", Artist = "Artist", ArtistCredits = ["Artist"], DurationSeconds = 180, Isrc = "USABC2400001" },
                new TrackMatchSearchCandidate { CandidateSource = "spotify", ExternalId = "two",
                    Title = "Song", Artist = "Artist", ArtistCredits = ["Artist"], DurationSeconds = 180, Isrc = "USABC2400002" }
            ]), CancellationToken.None);

        Assert.Null(id);
        Assert.Empty(await db.TrackSourceIds.ToListAsync());
    }

    [Theory]
    [InlineData("spotify", "youtube")]
    [InlineData("youtube", "spotify")]
    public async Task ResolveAsync_SearchesMissingProviderAndReusesSavedIdentity(string destination, string source)
    {
        await using var db = CreateDb();
        var entry = await SeedCanonicalAsync(db);
        entry.TrackObservation!.SourceType = source;
        await db.SaveChangesAsync();
        var searches = 0;
        Task<IReadOnlyList<TrackMatchSearchCandidate>> Search(TrackObservation _, CancellationToken ct)
        {
            searches++;
            return Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([
                Candidate("found", destination)
            ]);
        }

        Assert.Equal("found", await ResolveCanonicalAsync(db, entry, destination, Search));
        Assert.Equal("found", await ResolveCanonicalAsync(db, entry, destination, Search));
        Assert.Equal(1, searches);
        Assert.Equal(entry.TrackId, (await db.TrackSourceIds.SingleAsync()).TrackId);
    }

    [Theory]
    [InlineData("USABC2400001", "us-abc-24-00001", "one")]
    [InlineData("USABC2400001", "USABC2400002", null)]
    [InlineData(null, null, "one")]
    public async Task ResolveAsync_EquivalentReleasesDoNotCompeteButDistinctRecordingsDo(
        string? firstIsrc, string? secondIsrc, string? expected)
    {
        await using var db = CreateDb();
        var entry = await SeedCanonicalAsync(db);
        var first = Candidate("one", "spotify", firstIsrc);
        var second = Candidate("two", "spotify", secondIsrc);

        var actual = await ResolveCanonicalAsync(db, entry, "spotify",
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([first, second]));

        Assert.Equal(expected, actual);
        Assert.Equal(expected is null ? 0 : 1, await db.TrackSourceIds.CountAsync());
    }

    [Fact]
    public async Task ResolveAsync_KnownIsrcSelectsRecordingWithoutChangingSourceEvidence()
    {
        await using var db = CreateDb();
        var entry = await SeedCanonicalAsync(db);
        entry.Track!.Isrc = "us-abc-24-00002";
        await db.SaveChangesAsync();
        var other = Candidate("other-recording", "spotify", "USABC2400001");
        var correct = Candidate("correct-recording", "spotify", "USABC2400002");

        var id = await ResolveCanonicalAsync(db, entry, "spotify", (evidence, _) =>
        {
            Assert.Equal("USABC2400002", TrackObservationParser.ReadMetadata(evidence)?.Isrc);
            Assert.Equal("Song", evidence.Title);
            Assert.Equal(180, evidence.DurationSeconds);
            return Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([other, correct]);
        });

        Assert.Equal("correct-recording", id);
        Assert.Null((await db.TrackObservations.AsNoTracking().SingleAsync()).RawMetadata);
    }

    [Theory]
    [InlineData("Song (Live)", 180)]
    [InlineData("Song (Slowed)", 240)]
    public async Task ResolveAsync_KnownIsrcDoesNotOverrideVersionConflict(string title, int duration)
    {
        await using var db = CreateDb();
        var entry = await SeedCanonicalAsync(db);
        entry.Track!.Isrc = "USABC2400001";
        await db.SaveChangesAsync();
        var candidate = Candidate("conflicting", "spotify", "USABC2400001", title, duration);

        Assert.Null(await ResolveCanonicalAsync(db, entry, "spotify",
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([candidate])));
        Assert.Empty(await db.TrackSourceIds.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_ExistingCanonicalTrackGainsAcceptedArtistMetadataAndReusesItAfterRestart(bool mappingAppearsDuringSearch)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Guid entryId;
        Guid trackId;
        await using (var db = new ApplicationDbContext(options))
        {
            var entry = await SeedCanonicalAsync(db);
            entryId = entry.Id;
            trackId = entry.TrackId!.Value;
            entry.Track!.Isrc = "us-abc-24-00001";
            await new ArtistReconciliationService(db).ReconcileAsync(entry.Track!,
                [new ArtistCreditMetadata { Name = "Artist" }], "observation", DateTimeOffset.UtcNow, default);
            await db.SaveChangesAsync();
            var artistId = (await db.Artists.SingleAsync()).Id;
            var candidate = new TrackMatchSearchCandidate {
                CandidateSource = "spotify", ExternalId = "accepted", Title = "Song", Artist = "Artist", Isrc = "USABC2400001",
                ArtistCredits = ["Artist"], DurationSeconds = 180,
                ArtistIdentities = [new ArtistCreditMetadata { Name = "Artist", Provider = "spotify", ExternalId = "artist-id",
                    ProfileUrl = "https://open.spotify.com/artist/artist-id", ArtworkUrl = "https://images/artist",
                    RawMetadata = "{\"provider\":true}" }] };

            Assert.Equal("accepted", await ResolveCanonicalAsync(db, entry, "spotify",
                async (_, ct) =>
                {
                    if (mappingAppearsDuringSearch)
                    {
                        db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
                            SourceType = "spotify", ExternalId = "accepted" });
                        await db.SaveChangesAsync(ct);
                    }
                    return [candidate];
                }));
            Assert.Equal(artistId, (await db.Artists.SingleAsync()).Id);
        }
        await using var restarted = new ApplicationDbContext(options);
        var storedEntry = await restarted.PlaylistEntries.SingleAsync(e => e.Id == entryId);
        Assert.Equal("accepted", await ResolveCanonicalAsync(restarted, storedEntry, "spotify",
            (_, _) => throw new InvalidOperationException("Known mapping must not search again.")));
        Assert.Single(await restarted.Tracks.ToArrayAsync());
        Assert.Equal(trackId, (await restarted.TrackSourceIds.SingleAsync()).TrackId);
        var link = await restarted.ArtistProviderLinks.SingleAsync();
        Assert.Equal("artist-id", link.ExternalId);
        Assert.Equal("https://open.spotify.com/artist/artist-id", link.ProfileUrl);
        Assert.Equal("https://images/artist", link.ArtworkUrl);
        Assert.Equal("{\"provider\":true}", link.RawMetadata);
        var credit = await restarted.TrackArtistCredits.SingleAsync();
        using var provenance = JsonDocument.Parse(credit.Provenance!);
        Assert.Equal("artist-id", provenance.RootElement.GetProperty("spotify").GetProperty("ExternalId").GetString());
    }

    [Theory]
    [InlineData("empty", true)]
    [InlineData("anonymous", true)]
    [InlineData("stable", true)]
    [InlineData("empty", false)]
    [InlineData("anonymous", false)]
    [InlineData("stable", false)]
    public async Task ResolveAsync_DestinationOnlyChoicePreservesSharedArtistEvidence(
        string credits, bool canonicalHasRecordingId)
    {
        await using var db = CreateDb();
        var entry = await SeedCanonicalAsync(db);
        entry.Track!.Isrc = canonicalHasRecordingId ? "USABC2400002" : null;
        if (credits != "empty")
            await new ArtistReconciliationService(db).ReconcileAsync(entry.Track,
                [new ArtistCreditMetadata { Name = "Artist", Provider = credits == "stable" ? "spotify" : null,
                    ExternalId = credits == "stable" ? "canonical-artist" : null }],
                "observation", DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();
        var before = await ArtistEvidenceSnapshot.ReadAsync(db);
        var candidate = new TrackMatchSearchCandidate {
            CandidateSource = "spotify", ExternalId = "destination-B", Title = "Song", Artist = "Artist",
            ArtistCredits = ["Artist"], DurationSeconds = 180, Isrc = "USABC2400001",
            ArtistIdentities = [new ArtistCreditMetadata { Name = "Artist", Provider = "spotify",
                ExternalId = "different-artist", ArtworkUrl = "https://images/other-artist" }] };

        Assert.Equal("destination-B", await ResolveCanonicalAsync(db, entry, "spotify",
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([candidate])));

        Assert.Equal(before, await ArtistEvidenceSnapshot.ReadAsync(db));
    }

    [Fact]
    public async Task ResolveAsync_MappingInsertedDuringSearchDoesNotCorroborateItsOwnArtistEvidence()
    {
        await using var db = CreateDb();
        var entry = await SeedCanonicalAsync(db);
        var before = await ArtistEvidenceSnapshot.ReadAsync(db);
        var candidate = new TrackMatchSearchCandidate {
            CandidateSource = "spotify", ExternalId = "destination-B", Title = "Song", Artist = "Artist",
            ArtistCredits = ["Artist"], DurationSeconds = 180,
            ArtistIdentities = [new ArtistCreditMetadata { Name = "Artist", Provider = "spotify", ExternalId = "artist-B" }] };

        Assert.Equal("destination-B", await ResolveCanonicalAsync(db, entry, "spotify", async (_, ct) =>
        {
            db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = entry.TrackId!.Value,
                SourceType = "spotify", ExternalId = candidate.ExternalId });
            await db.SaveChangesAsync(ct);
            return [candidate];
        }));

        Assert.Equal(before, await ArtistEvidenceSnapshot.ReadAsync(db));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_UnresolvedSourceAcceptsArtistsForNewOrEstablishedRecording(bool established)
    {
        await using var db = CreateDb();
        var entry = await SeedUnresolvedAsync(db);
        var track = new Track { Id = Guid.NewGuid() };
        if (established)
        {
            db.Tracks.Add(track);
            db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = track.Id,
                SourceType = "spotify", ExternalId = "accepted" });
            await db.SaveChangesAsync();
        }
        var candidate = new TrackMatchSearchCandidate {
            CandidateSource = "spotify", ExternalId = "accepted", Title = "Song", Artist = "Artist",
            ArtistCredits = ["Artist"], DurationSeconds = 180,
            ArtistIdentities = [new ArtistCreditMetadata { Name = "Artist", Provider = "spotify", ExternalId = "artist-id" }] };
        var reconciler = new PlaylistCanonicalReconciliationService(db);

        Assert.Equal("accepted", await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            async (observationId, _, ct) =>
            {
                Assert.False(established);
                db.Tracks.Add(track);
                (await db.TrackObservations.SingleAsync(x => x.Id == observationId, ct)).TrackId = track.Id;
                await db.SaveChangesAsync(ct);
                return track.Id;
            },
            (observation, id, ct) => reconciler.ReconcileObservationAsync(observation, id, ct),
            new PlatformAccountContext(42, 7), "spotify", entry,
            (_, _) => Task.FromResult<IReadOnlyList<TrackMatchSearchCandidate>>([candidate]), default));

        Assert.Equal("artist-id", (await db.ArtistProviderLinks.SingleAsync()).ExternalId);
        Assert.Equal(track.Id, (await db.TrackArtistCredits.SingleAsync()).TrackId);
    }

    [Theory]
    [InlineData("empty", false, "mapping")]
    [InlineData("empty", true, "mapping")]
    [InlineData("anonymous", false, "mapping")]
    [InlineData("anonymous", true, "mapping")]
    [InlineData("stable", false, "mapping")]
    [InlineData("stable", true, "mapping")]
    [InlineData("anonymous", false, "isrc")]
    [InlineData("anonymous", false, "musicbrainz")]
    public async Task ResolveAsync_LocalTrackedFallbackDoesNotUseRecordingEvidenceAddedDuringSearch(
        string credits, bool separateContext, string lateEvidence)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Guid entryId;
        Guid trackId;
        await using (var setup = new ApplicationDbContext(options))
        {
            var entry = await SeedUnresolvedAsync(setup);
            entryId = entry.Id;
            var track = new Track { Id = Guid.NewGuid(), SearchTitle = "Song", SearchArtist = "Artist" };
            trackId = track.Id;
            setup.Tracks.Add(track);
            // The entry and observation stay unresolved. Production ResolveExistingAsync
            // must discover this global source mapping and return a tracked Track.
            setup.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
                SourceType = "youtube", ExternalId = "video1" });
            if (credits != "empty")
                await new ArtistReconciliationService(setup).ReconcileAsync(track,
                    [new ArtistCreditMetadata { Name = "Artist", Provider = credits == "stable" ? "spotify" : null,
                        ExternalId = credits == "stable" ? "canonical-artist" : null,
                        Role = TrackArtistRole.Featured, JoinPhrase = " feat. ",
                        ArtworkUrl = "https://images/canonical-artist" }],
                    "observation", DateTimeOffset.UtcNow, default);
            setup.ArtistReconciliationConflicts.Add(new ArtistReconciliationConflict {
                Id = Guid.NewGuid(), TrackId = trackId, EvidenceKey = "retained-conflict",
                Evidence = "{\"retained\":true}", Reason = "Retained import conflict", CreatedAt = DateTimeOffset.UtcNow });
            await setup.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        var storedEntry = await db.PlaylistEntries.SingleAsync(e => e.Id == entryId);
        Assert.Null(storedEntry.TrackId);
        Assert.Empty(db.Tracks.Local);
        var candidate = new TrackMatchSearchCandidate {
            CandidateSource = "spotify", ExternalId = "destination-B", Title = "Song", Artist = "Artist",
            ArtistCredits = ["Artist"], DurationSeconds = 180,
            Isrc = lateEvidence == "isrc" ? "USABC2400001" : null,
            MbidRecording = lateEvidence == "musicbrainz" ? "recording-b" : null,
            ArtistIdentities = [new ArtistCreditMetadata { Name = "Artist", Provider = "spotify", ExternalId = "artist-B",
                ArtworkUrl = "https://images/artist-B", RawMetadata = "{\"destination\":true}" }] };
        var before = await ArtistEvidenceSnapshot.ReadAsync(db);
        var reconciler = new PlaylistCanonicalReconciliationService(db);
        var searches = 0;

        var result = await PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("Must reuse the locally resolved Track."),
            (observation, id, ct) => reconciler.ReconcileObservationAsync(observation, id, ct),
            new PlatformAccountContext(42, 7), "spotify", storedEntry,
            async (_, ct) =>
            {
                searches++;
                var trackedTrack = Assert.Single(db.Tracks.Local);
                Assert.Equal(trackId, trackedTrack.Id);
                Assert.Equal(EntityState.Unchanged, db.Entry(trackedTrack).State);
                Assert.Null(trackedTrack.Isrc);
                Assert.Null(trackedTrack.MbidRecording);
                Assert.DoesNotContain(trackedTrack.SourceIds, s => s.SourceType == "spotify");
                if (separateContext)
                {
                    await using var writer = new ApplicationDbContext(options);
                    writer.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
                        SourceType = "spotify", ExternalId = candidate.ExternalId });
                    await writer.SaveChangesAsync(ct);
                    // Fixup happens later, when the production post-search lookup reads it.
                    Assert.DoesNotContain(trackedTrack.SourceIds, s => s.SourceType == "spotify");
                }
                else
                {
                    if (lateEvidence == "mapping")
                        db.TrackSourceIds.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = trackId,
                            SourceType = "spotify", ExternalId = candidate.ExternalId });
                    else if (lateEvidence == "isrc") trackedTrack.Isrc = candidate.Isrc;
                    else trackedTrack.MbidRecording = candidate.MbidRecording;
                    await db.SaveChangesAsync(ct);
                }
                // Include deliberate scalar changes in the baseline, but no artist mutation.
                if (lateEvidence == "mapping")
                    Assert.Equal(before, await ArtistEvidenceSnapshot.ReadAsync(db));
                before = await ArtistEvidenceSnapshot.ReadAsync(db);
                return [candidate];
            }, default);

        Assert.Equal("destination-B", result);
        Assert.Equal(1, searches);
        Assert.Equal(trackId, (await db.PlaylistEntries.AsNoTracking().SingleAsync(e => e.Id == entryId)).TrackId);
        Assert.Contains(Assert.Single(db.Tracks.Local).SourceIds,
            s => s.SourceType == "spotify" && s.ExternalId == candidate.ExternalId);
        Assert.Equal("artist-B", Assert.Single(candidate.ArtistIdentities).ExternalId);
        Assert.Equal(before, await ArtistEvidenceSnapshot.ReadAsync(db));
    }

    private static TrackMatchSearchCandidate Candidate(string id, string service,
        string? isrc = null, string title = "Song", int duration = 180) => new()
    {
        CandidateSource = service, ExternalId = id, Title = title, Artist = "Artist",
        ArtistCredits = ["Artist"], DurationSeconds = duration, Isrc = isrc
    };

    private static Task<string?> ResolveCanonicalAsync(ApplicationDbContext db, PlaylistEntry entry, string service,
        Func<TrackObservation, CancellationToken, Task<IReadOnlyList<TrackMatchSearchCandidate>>> search) =>
        PlaylistDestinationIdentityResolver.ResolveAsync(db, new TrackMatchingOptions(),
            (_, _, _) => throw new InvalidOperationException("Already canonical."),
            (_, _, _) => throw new InvalidOperationException("Already canonical."),
            new PlatformAccountContext(42, 7), service, entry, search, CancellationToken.None);

    private static async Task<PlaylistEntry> SeedCanonicalAsync(ApplicationDbContext db)
    {
        var entry = await SeedUnresolvedAsync(db);
        entry.Track = new Track { Id = Guid.NewGuid(),
            CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata
            { Title = "Song", Artist = "Artist", DurationSeconds = 180 }) };
        db.Tracks.Add(entry.Track);
        entry.TrackId = entry.Track.Id;
        entry.TrackObservation!.TrackId = entry.Track.Id;
        await db.SaveChangesAsync();
        return entry;
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<PlaylistEntry> SeedUnresolvedAsync(ApplicationDbContext db)
    {
        var playlist = new Playlist { Id = Guid.NewGuid(), UserId = 42, Name = "Road songs" };
        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video1",
            Title = "Song", Artist = "Artist", DurationSeconds = 180,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var entry = new PlaylistEntry
        {
            Id = Guid.NewGuid(), PlaylistId = playlist.Id, Playlist = playlist,
            TrackObservationId = observation.Id, TrackObservation = observation
        };
        db.PlaylistEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }
}
