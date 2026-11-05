using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Spotify;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Cantaro.Api.Tests;

[Collection("Artist reconciliation PostgreSQL")]
public sealed class ArtistReconciliationPostgresTests
{
    private const string Mbid = "0d5f4b04-3be8-427a-8c24-23b1454b2f31";

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExistingCreditlessTrackRace_ReloadsWinningCreditWithoutLosingPendingImport(
        bool distinctIdentity, bool explicitTransaction)
    {
        await using var schema = await Schema.CreateAsync();
        Guid trackId;
        Guid observationId;
        Guid artistId;
        var incoming = Identity() with { Role = TrackArtistRole.Featured, JoinPhrase = " feat. " };
        var alternative = incoming with { ExternalId = "different", ProfileUrl = "https://open.spotify.com/artist/different" };
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            var track = await new SpotifyTrackResolver(setup).ResolveAsync(Snapshot("existing") with {
                ArtistIdentities = [incoming] }, DateTimeOffset.UtcNow, default);
            // Simulate a legacy track whose provider identity exists but credits have not been populated.
            artistId = track.ArtistCredits.Single().ArtistId;
            setup.TrackArtistCredits.RemoveRange(track.ArtistCredits.ToArray());
            var otherArtist = new Artist { Id = Guid.NewGuid(), Name = incoming.Name };
            setup.Artists.Add(otherArtist);
            setup.ArtistProviderLinks.Add(new ArtistProviderLink { Id = Guid.NewGuid(), Artist = otherArtist,
                Provider = "spotify", ExternalId = alternative.ExternalId! });
            var observation = Observation("existing", track.Id);
            setup.TrackObservations.Add(observation);
            await setup.SaveChangesAsync();
            trackId = track.Id;
            observationId = observation.Id;
        }

        var failures = new IdentityFailures();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options).AddInterceptors(failures).Options;
        await using var winner = new ApplicationDbContext(schema.Options);
        await using var loser = new ApplicationDbContext(options);
        await using var transaction = explicitTransaction ? await loser.Database.BeginTransactionAsync() : null;
        TrackObservation? precedingObservation = null;
        if (transaction != null)
        {
            precedingObservation = Observation("before-import", trackId);
            loser.TrackObservations.Add(precedingObservation);
            await loser.SaveChangesAsync();
        }
        var previousAttempts = failures.Attempts;
        var updatedObservation = await loser.TrackObservations.SingleAsync(o => o.Id == observationId);
        updatedObservation.Title = "Refreshed observation";
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var winningTrack = await new SpotifyTrackResolver(winner).ResolveAsync(Snapshot("existing") with {
            ArtistIdentities = [incoming] }, now, default);
        var losingTrack = await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("existing") with {
            ArtistIdentities = [distinctIdentity ? alternative : incoming] }, now.AddSeconds(1), default);
        Assert.Equal(trackId, winningTrack.Id);
        Assert.Equal(trackId, losingTrack.Id);
        var tentativeCreditId = losingTrack.ArtistCredits.Single().Id;
        var winningCreditId = winningTrack.ArtistCredits.Single().Id;
        var pendingTrack = await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("pending"), now, default);
        var pendingObservation = Observation("pending", pendingTrack.Id);
        loser.TrackObservations.Add(pendingObservation);
        await winner.SaveChangesAsync();

        await loser.SaveChangesAsync();
        if (transaction != null)
        {
            // Retry must roll back only its savepoint, retain the earlier save,
            // and leave the outer transaction active and uncommitted.
            Assert.Same(transaction, loser.Database.CurrentTransaction);
            Assert.True(await loser.TrackObservations.AnyAsync(o => o.Id == precedingObservation!.Id));
            await using var outside = new ApplicationDbContext(schema.Options);
            Assert.False(await outside.TrackObservations.AnyAsync(o => o.Id == precedingObservation!.Id));
            await transaction.CommitAsync();
        }

        await using var verify = new ApplicationDbContext(schema.Options);
        var credit = await verify.TrackArtistCredits.SingleAsync(c => c.TrackId == trackId);
        Assert.Equal(winningCreditId, credit.Id);
        Assert.NotEqual(tentativeCreditId, credit.Id);
        Assert.Equal(artistId, credit.ArtistId);
        Assert.Equal(0, credit.Position);
        Assert.Equal(TrackArtistRole.Featured, credit.Role);
        Assert.Equal(incoming.JoinPhrase, credit.JoinPhrase);
        Assert.Contains("shared", credit.Provenance!);
        Assert.Equal(winningCreditId, losingTrack.ArtistCredits.Single().Id);
        Assert.Equal(previousAttempts + 2, failures.Attempts);
        Assert.Equal([distinctIdentity ? "IX_TrackArtistCredits_TrackId_Position"
            : "IX_TrackArtistCredits_TrackId_ArtistId_Role"], failures.Constraints);
        var conflicts = await verify.ArtistReconciliationConflicts.ToArrayAsync();
        if (distinctIdentity)
        {
            var conflict = Assert.Single(conflicts);
            Assert.Equal(trackId, conflict.TrackId);
            Assert.Contains("different", conflict.Evidence);
            Assert.Contains(artistId.ToString(), conflict.Evidence);
        }
        else Assert.Empty(conflicts);
        Assert.Equal(2, await verify.Artists.CountAsync());
        Assert.Equal(2, await verify.ArtistProviderLinks.CountAsync());
        Assert.Equal("Refreshed observation", (await verify.TrackObservations.SingleAsync(o => o.Id == observationId)).Title);
        Assert.Equal(pendingTrack.Id, (await verify.TrackObservations.SingleAsync(o => o.Id == pendingObservation.Id)).TrackId);
        if (precedingObservation != null)
            Assert.True(await verify.TrackObservations.AnyAsync(o => o.Id == precedingObservation.Id));
        Assert.Equal(2, await verify.Tracks.CountAsync());
        Assert.Equal(2, await verify.Songs.CountAsync());
        Assert.Equal(2, await verify.SongTracks.CountAsync());
        Assert.Equal(2, await verify.TrackSourceIds.CountAsync());
        Assert.Equal(now.AddSeconds(1), (await verify.TrackSourceIds.SingleAsync(s => s.TrackId == trackId)).LastVerifiedAt);
        Assert.DoesNotContain(loser.ChangeTracker.Entries(), e => e.State == EntityState.Added);

        // Repeated import on the same context must reuse the winner and deduplicate conflicts.
        await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("existing") with {
            ArtistIdentities = [distinctIdentity ? alternative : incoming] }, now.AddSeconds(2), default);
        updatedObservation.Title = "Updated after retry";
        await loser.SaveChangesAsync();
        Assert.Equal(winningCreditId, (await verify.TrackArtistCredits.AsNoTracking().SingleAsync(c => c.TrackId == trackId)).Id);
        Assert.Equal(distinctIdentity ? 1 : 0, await verify.ArtistReconciliationConflicts.CountAsync());
        Assert.Equal("Updated after retry", (await verify.TrackObservations.AsNoTracking().SingleAsync(o => o.Id == observationId)).Title);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpotifyImports_SeparateContextsRetryRealIdentityCollisionWithoutLosingPendingImport(bool explicitTransaction)
    {
        await using var schema = await Schema.CreateAsync();
        var failures = new IdentityFailures();
        var loserOptions = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options).AddInterceptors(failures).Options;
        await using var first = new ApplicationDbContext(schema.Options);
        await using var second = new ApplicationDbContext(loserOptions);
        await using var transaction = explicitTransaction ? await second.Database.BeginTransactionAsync() : null;
        var now = DateTimeOffset.UtcNow;
        // Both contexts resolve before either saves, as imports of different playlists do.
        var firstTrack = await new SpotifyTrackResolver(first).ResolveAsync(Snapshot("one"), now, default);
        var secondTrack = await new SpotifyTrackResolver(second).ResolveAsync(Snapshot("two"), now, default);
        var thirdTrack = await new SpotifyTrackResolver(second).ResolveAsync(Snapshot("three"), now, default);
        var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = "two",
            Title = "Song two", Artist = "Shared artist", TrackId = secondTrack.Id, MatchStatus = TrackMatchingStatuses.Matched };
        second.TrackObservations.Add(observation);
        await first.SaveChangesAsync();
        // Exercise both public async SaveChanges overloads, including an outer transaction/savepoint.
        if (explicitTransaction) await second.SaveChangesAsync(true, default);
        else await second.SaveChangesAsync();
        if (transaction != null) await transaction.CommitAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        var artist = await verify.Artists.SingleAsync();
        Assert.Equal(artist.Id, (await verify.ArtistProviderLinks.SingleAsync()).ArtistId);
        var credits = await verify.TrackArtistCredits.ToArrayAsync();
        Assert.Equal(3, credits.Length);
        Assert.All(credits, c => Assert.Equal(artist.Id, c.ArtistId));
        Assert.Contains(credits, c => c.TrackId == firstTrack.Id);
        Assert.Contains(credits, c => c.TrackId == secondTrack.Id);
        Assert.Contains(credits, c => c.TrackId == thirdTrack.Id);
        Assert.Equal(3, await verify.Songs.CountAsync());
        Assert.Equal(3, await verify.SongTracks.CountAsync());
        Assert.Equal(3, await verify.TrackSourceIds.CountAsync());
        Assert.Equal(secondTrack.Id, (await verify.TrackObservations.SingleAsync()).TrackId);
        Assert.Equal(2, failures.Attempts);
        Assert.Equal(["IX_ArtistProviderLinks_Provider_ExternalId"], failures.Constraints);
        Assert.Empty(await verify.ArtistReconciliationConflicts.ToArrayAsync());
        Assert.Equal(artist.Id, secondTrack.ArtistCredits.Single().ArtistId);
        Assert.DoesNotContain(second.ChangeTracker.Entries<Artist>(), e => e.State == EntityState.Added);

        // The same caller context remains usable and the completed retry is not replayed on a later save.
        observation.Title = "Updated observation";
        await second.SaveChangesAsync();
        Assert.Equal("Updated observation", (await verify.TrackObservations.AsNoTracking().SingleAsync()).Title);
        Assert.Equal(3, await verify.TrackArtistCredits.CountAsync());
    }

    [PostgresFact]
    public async Task MusicBrainzAcceptance_RetriesArtistIdentityCollisionThroughActualMatchingSave()
    {
        await using var schema = await Schema.CreateAsync();
        Guid observationId;
        Guid candidateId;
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            var observation = new TrackObservation { Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = "video",
                Title = "Together", Artist = "Shared artist", DurationSeconds = 180, MatchStatus = TrackMatchingStatuses.Pending };
            var candidate = new TrackResolutionCandidate { Id = Guid.NewGuid(), TrackObservationId = observation.Id,
                CandidateSource = "musicbrainz", ExternalId = "recording", MbidRecording = "recording", Title = "Together",
                Artist = "Shared artist", Score = 1m,
                RawMetadata = System.Text.Json.JsonSerializer.Serialize(new TrackMatchCandidateStoredMetadata {
                    ArtistIdentities = [new ArtistCreditMetadata { Name = "Shared artist", Provider = "musicbrainz",
                        ExternalId = Mbid, MusicBrainzArtistId = Mbid }] }) };
            observation.Candidates.Add(candidate);
            setup.TrackObservations.Add(observation);
            await setup.SaveChangesAsync();
            observationId = observation.Id;
            candidateId = candidate.Id;
        }
        var failures = new IdentityFailures(async () =>
        {
            await using var winner = new ApplicationDbContext(schema.Options);
            var track = new Track { Id = Guid.NewGuid() };
            winner.Tracks.Add(track);
            await new ArtistReconciliationService(winner).ReconcileAsync(track,
                [new ArtistCreditMetadata { Name = "Shared artist", Provider = "musicbrainz", ExternalId = Mbid,
                    MusicBrainzArtistId = Mbid }], "musicbrainz", DateTimeOffset.UtcNow, default);
            await winner.SaveChangesAsync();
        });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options).AddInterceptors(failures).Options;
        await using var db = new ApplicationDbContext(options);
        var matcher = new TrackMatchingService(db, [], NullLogger<TrackMatchingService>.Instance);
        await matcher.AcceptCandidateAsync(observationId, candidateId, default);

        await using var verify = new ApplicationDbContext(schema.Options);
        var artist = await verify.Artists.SingleAsync();
        Assert.Equal(Mbid, artist.MusicBrainzArtistId);
        Assert.Equal(2, await verify.TrackArtistCredits.CountAsync());
        Assert.All(await verify.TrackArtistCredits.ToArrayAsync(), c => Assert.Equal(artist.Id, c.ArtistId));
        Assert.Single(await verify.ArtistProviderLinks.ToArrayAsync());
        Assert.Equal(2, failures.Attempts);
        Assert.Equal(["IX_Artists_MusicBrainzArtistId"], failures.Constraints);
        var matched = await verify.TrackObservations.SingleAsync();
        Assert.NotNull(matched.TrackId);
        Assert.Equal(TrackMatchingStatuses.Matched, matched.MatchStatus);
    }

    [PostgresFact]
    public async Task IdentityRace_WithConflictingWinnerRemainsReviewableWithoutMerging()
    {
        await using var schema = await Schema.CreateAsync();
        await using var winner = new ApplicationDbContext(schema.Options);
        await using var loser = new ApplicationDbContext(schema.Options);
        var firstTrack = await new SpotifyTrackResolver(winner).ResolveAsync(Snapshot("one") with {
            ArtistIdentities = [Identity() with { MusicBrainzArtistId = Mbid }] }, DateTimeOffset.UtcNow, default);
        var secondTrack = await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("two") with {
            ArtistIdentities = [Identity() with { MusicBrainzArtistId = "82e3e1de-9243-4e58-b2d2-796c64f76c82" }] },
            DateTimeOffset.UtcNow, default);
        await winner.SaveChangesAsync();
        await loser.SaveChangesAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        Assert.Equal(Mbid, (await verify.Artists.SingleAsync()).MusicBrainzArtistId);
        Assert.Equal(firstTrack.Id, (await verify.TrackArtistCredits.SingleAsync()).TrackId);
        Assert.Empty(secondTrack.ArtistCredits);
        var conflict = await verify.ArtistReconciliationConflicts.SingleAsync();
        Assert.Equal(secondTrack.Id, conflict.TrackId);
        Assert.Contains("82e3e1de-9243-4e58-b2d2-796c64f76c82", conflict.Evidence);
        Assert.Equal(2, await verify.TrackSourceIds.CountAsync());
        Assert.Single(await verify.ArtistProviderLinks.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingCreditPromotionRace_ReplaysOriginalCreditWithoutMergingIdentifiedArtists(bool identified)
    {
        await using var schema = await Schema.CreateAsync();
        Guid trackId;
        Guid originalArtistId;
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            var original = await new SpotifyTrackResolver(setup).ResolveAsync(Snapshot("two") with {
                ArtistIdentities = [new ArtistCreditMetadata { Name = "Shared artist",
                    MusicBrainzArtistId = identified ? Mbid : null }] }, DateTimeOffset.UtcNow, default);
            await setup.SaveChangesAsync();
            trackId = original.Id;
            originalArtistId = original.ArtistCredits.Single().ArtistId;
        }
        await using var loser = new ApplicationDbContext(schema.Options);
        await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("two"), DateTimeOffset.UtcNow, default);
        await using (var winner = new ApplicationDbContext(schema.Options))
        {
            await new SpotifyTrackResolver(winner).ResolveAsync(Snapshot("one"), DateTimeOffset.UtcNow, default);
            await winner.SaveChangesAsync();
        }
        await loser.SaveChangesAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        var providerArtist = (await verify.ArtistProviderLinks.SingleAsync(l => l.Provider == "spotify")).ArtistId;
        var credit = await verify.TrackArtistCredits.SingleAsync(c => c.TrackId == trackId);
        Assert.Equal(identified ? originalArtistId : providerArtist, credit.ArtistId);
        Assert.Equal(identified ? 1 : 0, await verify.ArtistReconciliationConflicts.CountAsync());
        Assert.Equal(2, await verify.Artists.CountAsync());
        Assert.False(await verify.ArtistProviderLinks.AnyAsync(l => l.ArtistId == originalArtistId && l.Provider == "spotify"));
    }

    [PostgresFact]
    public async Task ConcurrentDifferentIdsOnSameAnonymousArtist_PreserveWinningLinkAndRecordConflict()
    {
        await using var schema = await Schema.CreateAsync();
        Guid trackId;
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            var track = await new SpotifyTrackResolver(setup).ResolveAsync(Snapshot("one") with {
                ArtistIdentities = [new ArtistCreditMetadata { Name = "Shared artist" }] }, DateTimeOffset.UtcNow, default);
            await setup.SaveChangesAsync();
            trackId = track.Id;
        }
        var failures = new IdentityFailures();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options).AddInterceptors(failures).Options;
        await using var winner = new ApplicationDbContext(schema.Options);
        await using var loser = new ApplicationDbContext(options);
        await new SpotifyTrackResolver(winner).ResolveAsync(Snapshot("one"), DateTimeOffset.UtcNow, default);
        await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("one") with {
            ArtistIdentities = [Identity() with { ExternalId = "different" }] }, DateTimeOffset.UtcNow, default);
        await winner.SaveChangesAsync();
        await loser.SaveChangesAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        Assert.Single(await verify.Artists.ToArrayAsync());
        Assert.Equal("shared", (await verify.ArtistProviderLinks.SingleAsync()).ExternalId);
        Assert.Equal(trackId, (await verify.TrackArtistCredits.SingleAsync()).TrackId);
        var conflict = await verify.ArtistReconciliationConflicts.SingleAsync();
        Assert.Contains("different", conflict.Evidence);
        Assert.Equal(2, failures.Attempts);
        Assert.Equal(["IX_ArtistProviderLinks_ArtistId_Provider"], failures.Constraints);
    }

    [PostgresFact]
    public async Task LegacyAggregateReplacementRace_ReplaysDeletedAndAddedCreditsAsOneCollaboration()
    {
        await using var schema = await Schema.CreateAsync();
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            await new SpotifyTrackResolver(setup).ResolveAsync(Snapshot("two") with {
                Artist = "Alpha feat. Beta", ArtistNames = ["Alpha feat. Beta"],
                ArtistIdentities = [new ArtistCreditMetadata { Name = "Alpha feat. Beta" }] }, DateTimeOffset.UtcNow, default);
            await setup.SaveChangesAsync();
        }
        await using var winner = new ApplicationDbContext(schema.Options);
        await using var loser = new ApplicationDbContext(schema.Options);
        var alpha = Identity() with { Name = "Alpha", ExternalId = "alpha" };
        var beta = Identity() with { Name = "Beta", ExternalId = "beta" };
        await new SpotifyTrackResolver(winner).ResolveAsync(Snapshot("one") with {
            Artist = "Alpha", ArtistNames = ["Alpha"], ArtistIdentities = [alpha] }, DateTimeOffset.UtcNow, default);
        var track = await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("two") with {
            Artist = "Alpha, Beta", ArtistNames = ["Alpha", "Beta"], ArtistIdentities = [alpha, beta] }, DateTimeOffset.UtcNow, default);
        await winner.SaveChangesAsync();
        await loser.SaveChangesAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        var credits = await verify.TrackArtistCredits.Where(c => c.TrackId == track.Id).OrderBy(c => c.Position).ToArrayAsync();
        Assert.Equal(["Alpha", "Beta"], credits.Select(c => c.CreditedName));
        Assert.Equal(3, await verify.Artists.CountAsync()); // Original anonymous row is retained for other references.
        Assert.Equal(2, await verify.ArtistProviderLinks.CountAsync());
        Assert.Empty(await verify.ArtistReconciliationConflicts.ToArrayAsync());
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConcurrentConflictEvidence_DeduplicatesWithoutLosingPendingImport(bool reordered, bool explicitTransaction)
    {
        await using var schema = await Schema.CreateAsync();
        var alpha = Identity() with { Name = "Alpha", ExternalId = "alpha" };
        var beta = Identity() with { Name = "Beta", ExternalId = "beta" };
        var original = Snapshot("existing") with {
            Artist = "Alpha, Beta", ArtistNames = ["Alpha", "Beta"], ArtistIdentities = [alpha, beta] };
        Guid trackId;
        Guid observationId;
        Guid[] canonicalArtists;
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            var track = await new SpotifyTrackResolver(setup).ResolveAsync(original, DateTimeOffset.UtcNow, default);
            var observation = Observation("existing", track.Id);
            setup.TrackObservations.Add(observation);
            await setup.SaveChangesAsync();
            trackId = track.Id;
            observationId = observation.Id;
            canonicalArtists = track.ArtistCredits.OrderBy(c => c.Position).Select(c => c.ArtistId).ToArray();
        }

        var failures = new IdentityFailures();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options).AddInterceptors(failures).Options;
        await using var winner = new ApplicationDbContext(schema.Options);
        await using var loser = new ApplicationDbContext(options);
        await using var transaction = explicitTransaction ? await loser.Database.BeginTransactionAsync() : null;
        TrackObservation? precedingObservation = null;
        if (explicitTransaction)
        {
            precedingObservation = Observation("before-import", trackId);
            loser.TrackObservations.Add(precedingObservation);
            await loser.SaveChangesAsync();
        }
        var previousAttempts = failures.Attempts;
        var updatedObservation = await loser.TrackObservations.SingleAsync(o => o.Id == observationId);
        updatedObservation.Title = "Refreshed observation";
        var incoming = reordered ? original with {
            Artist = "Beta, Alpha", ArtistNames = ["Beta", "Alpha"], ArtistIdentities = [beta, alpha] }
            : original with { ArtistIdentities = [alpha with { ExternalId = "different-alpha" }, beta] };
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        // Both playlist refreshes stage the same evidence before either commits it.
        await new SpotifyTrackResolver(winner).ResolveAsync(incoming, now, default);
        await new SpotifyTrackResolver(loser).ResolveAsync(incoming, now.AddSeconds(1), default);
        var evidence = loser.ArtistReconciliationConflicts.Local.Select(c => c.Evidence).Order().ToArray();
        Assert.Equal(reordered ? 2 : 1, evidence.Length);
        var pendingTrack = await new SpotifyTrackResolver(loser).ResolveAsync(Snapshot("pending"), now, default);
        var pendingObservation = Observation("pending", pendingTrack.Id);
        loser.TrackObservations.Add(pendingObservation);
        await winner.SaveChangesAsync();
        var winningConflictIds = winner.ArtistReconciliationConflicts.Local.Select(c => c.Id).Order().ToArray();

        await loser.SaveChangesAsync();
        if (transaction != null) await transaction.CommitAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        var conflicts = await verify.ArtistReconciliationConflicts.ToArrayAsync();
        Assert.Equal(winningConflictIds, conflicts.Select(c => c.Id).Order());
        Assert.Equal(evidence, conflicts.Select(c => c.Evidence).Order());
        Assert.Equal(previousAttempts + 2, failures.Attempts);
        Assert.Equal(["IX_ArtistReconciliationConflicts_TrackId_EvidenceKey"], failures.Constraints);
        var canonical = await verify.TrackArtistCredits.Where(c => c.TrackId == trackId).OrderBy(c => c.Position).ToArrayAsync();
        Assert.Equal(canonicalArtists, canonical.Select(c => c.ArtistId));
        Assert.Equal([0, 1], canonical.Select(c => c.Position));
        Assert.Equal("Refreshed observation", (await verify.TrackObservations.SingleAsync(o => o.Id == observationId)).Title);
        Assert.Equal(pendingTrack.Id, (await verify.TrackObservations.SingleAsync(o => o.Id == pendingObservation.Id)).TrackId);
        if (precedingObservation != null)
            Assert.True(await verify.TrackObservations.AnyAsync(o => o.Id == precedingObservation.Id));
        Assert.Equal(2, await verify.Tracks.CountAsync());
        Assert.Equal(2, await verify.Songs.CountAsync());
        Assert.Equal(2, await verify.SongTracks.CountAsync());
        Assert.Equal(2, await verify.TrackSourceIds.CountAsync());
        Assert.Equal(now.AddSeconds(1), (await verify.TrackSourceIds.SingleAsync(s => s.TrackId == trackId)).LastVerifiedAt);
        Assert.Equal(3, await verify.ArtistProviderLinks.CountAsync());
        Assert.DoesNotContain(loser.ChangeTracker.Entries<ArtistReconciliationConflict>(), e => e.State == EntityState.Added);

        // Successful retry clears the replay state and leaves the caller usable.
        updatedObservation.Title = "Updated after retry";
        await loser.SaveChangesAsync();
        Assert.Equal("Updated after retry", (await verify.TrackObservations.AsNoTracking().SingleAsync(o => o.Id == observationId)).Title);
        Assert.Equal(winningConflictIds, (await verify.ArtistReconciliationConflicts.ToArrayAsync()).Select(c => c.Id).Order());
    }

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EvidenceAndIdentityCollisions_InEitherOrderPreservePendingImport(bool identityFirst, bool explicitTransaction)
    {
        await using var schema = await Schema.CreateAsync();
        var original = Snapshot("existing");
        Guid trackId;
        await using (var setup = new ApplicationDbContext(schema.Options))
        {
            var track = await new SpotifyTrackResolver(setup).ResolveAsync(original, DateTimeOffset.UtcNow, default);
            await setup.SaveChangesAsync();
            trackId = track.Id;
        }
        var now = DateTimeOffset.UtcNow;
        var conflicting = original with { ArtistIdentities = [Identity() with { ExternalId = "conflicting" }] };
        var pending = Snapshot("pending") with { Artist = "New artist", ArtistNames = ["New artist"],
            ArtistIdentities = [Identity() with { Name = "New artist", ExternalId = "new-artist" }] };
        await using var evidenceWinner = new ApplicationDbContext(schema.Options);
        await using var identityWinner = new ApplicationDbContext(schema.Options);
        await new SpotifyTrackResolver(evidenceWinner).ResolveAsync(conflicting, now, default);
        var winningTrack = await new SpotifyTrackResolver(identityWinner).ResolveAsync(pending with {
            Id = "winner", Name = "Another song" }, now, default);
        var winningConflictId = evidenceWinner.ArtistReconciliationConflicts.Local.Single().Id;
        var failures = new IdentityFailures(beforeSecondSave: async () =>
        {
            // The second writer commits after replay, forcing a different real
            // uniqueness failure on the next attempt of the same caller save.
            if (identityFirst) await evidenceWinner.SaveChangesAsync();
            else await identityWinner.SaveChangesAsync();
        });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(schema.Options).AddInterceptors(failures).Options;
        await using var loser = new ApplicationDbContext(options);
        await using var transaction = explicitTransaction ? await loser.Database.BeginTransactionAsync() : null;
        await new SpotifyTrackResolver(loser).ResolveAsync(conflicting, now, default);
        var pendingTrack = await new SpotifyTrackResolver(loser).ResolveAsync(pending, now, default);
        var observation = Observation("pending", pendingTrack.Id);
        loser.TrackObservations.Add(observation);
        if (identityFirst) await identityWinner.SaveChangesAsync();
        else await evidenceWinner.SaveChangesAsync();

        await loser.SaveChangesAsync();
        if (transaction != null) await transaction.CommitAsync();

        await using var verify = new ApplicationDbContext(schema.Options);
        Assert.Equal(3, failures.Attempts);
        string evidenceConstraint = "IX_ArtistReconciliationConflicts_TrackId_EvidenceKey";
        string identityConstraint = "IX_ArtistProviderLinks_Provider_ExternalId";
        Assert.Equal(identityFirst ? [identityConstraint, evidenceConstraint] : new[] { evidenceConstraint, identityConstraint }, failures.Constraints);
        var conflict = await verify.ArtistReconciliationConflicts.SingleAsync();
        Assert.Equal(winningConflictId, conflict.Id);
        Assert.Equal(trackId, conflict.TrackId);
        Assert.Contains("conflicting", conflict.Evidence);
        Assert.Equal(2, await verify.Artists.CountAsync());
        Assert.Equal(2, await verify.ArtistProviderLinks.CountAsync());
        var artistId = (await verify.ArtistProviderLinks.SingleAsync(l => l.ExternalId == "new-artist")).ArtistId;
        Assert.Equal(artistId, (await verify.TrackArtistCredits.SingleAsync(c => c.TrackId == pendingTrack.Id)).ArtistId);
        Assert.Equal(artistId, (await verify.TrackArtistCredits.SingleAsync(c => c.TrackId == winningTrack.Id)).ArtistId);
        Assert.Equal(pendingTrack.Id, (await verify.TrackObservations.SingleAsync()).TrackId);
        Assert.Equal(3, await verify.Tracks.CountAsync());
        Assert.Equal(3, await verify.Songs.CountAsync());
        Assert.Equal(3, await verify.SongTracks.CountAsync());
        Assert.Equal(3, await verify.TrackSourceIds.CountAsync());
        Assert.DoesNotContain(loser.ChangeTracker.Entries(), e => e.State == EntityState.Added);
    }

    private static TrackObservation Observation(string externalId, Guid trackId) => new() {
        Id = Guid.NewGuid(), SourceType = "spotify", ExternalId = externalId, Title = $"Observation {externalId}",
        Artist = "Shared artist", TrackId = trackId, MatchStatus = TrackMatchingStatuses.Matched };

    private static ArtistCreditMetadata Identity() => new() { Name = "Shared artist", Provider = "spotify",
        ExternalId = "shared", ProfileUrl = "https://open.spotify.com/artist/shared" };
    private static SpotifyTrackSnapshot Snapshot(string id) =>
        new(id, $"Song {id}", "Shared artist", ["Shared artist"], null, null, $"https://open.spotify.com/track/{id}", null, null, 180, null, 0)
        { ArtistIdentities = [Identity()] };

    private sealed class IdentityFailures(Func<Task>? beforeFirstSave = null, Func<Task>? beforeSecondSave = null) : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public List<string?> Constraints { get; } = [];
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts == 1 && beforeFirstSave != null) await beforeFirstSave();
            if (Attempts == 2 && beforeSecondSave != null) await beforeSecondSave();
            return result;
        }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg })
                Constraints.Add(pg.ConstraintName);
            return Task.CompletedTask;
        }
    }

    private sealed class Schema(string connectionString, string name, DbContextOptions<ApplicationDbContext> options) : IAsyncDisposable
    {
        public DbContextOptions<ApplicationDbContext> Options { get; } = options;
        public static async Task<Schema> CreateAsync()
        {
            var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES"));
            if (connection.Host is not ("localhost" or "127.0.0.1" or "::1")
                || connection.Database?.Contains("test", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidOperationException("CANTARO_TEST_POSTGRES must be a loopback test database.");
            var schemaName = $"cantaro_artist_test_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(connection.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await command.ExecuteNonQueryAsync();
            var scoped = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { SearchPath = schemaName };
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(scoped.ConnectionString).Options;
            var schema = new Schema(connection.ConnectionString, schemaName, options);
            try
            {
                await using var db = new ApplicationDbContext(options);
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                return schema;
            }
            catch { await schema.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{name}\" CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
                Skip = "Set CANTARO_TEST_POSTGRES to a loopback test database.";
        }
    }
    private sealed class PostgresTheoryAttribute : TheoryAttribute
    {
        public PostgresTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANTARO_TEST_POSTGRES")))
                Skip = "Set CANTARO_TEST_POSTGRES to a loopback test database.";
        }
    }
}

// Other PostgreSQL fixtures use database-wide EnsureCreated checks. Avoid
// overlapping their setup while these tests create temporary schemas.
[CollectionDefinition("Artist reconciliation PostgreSQL", DisableParallelization = true)]
public sealed class ArtistReconciliationPostgresCollection;
