using System.Data.Common;
using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Cantaro.Api.Tests;

public sealed partial class PlaylistUnmatchedReviewTests
{
    private readonly ITestOutputHelper _output;

    public PlaylistUnmatchedReviewTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ListAsync_BoundsRelationalCommandsForDistinctTracksAndDuplicateEntryProofs()
    {
        var small = await MeasureListCommandsAsync(8);
        var large = await MeasureListCommandsAsync(300);
        _output.WriteLine($"ListAsync commands: 8 distinct tracks = {small}; 300 distinct tracks = {large} (four entries per track).");
        Assert.True(small <= 8, $"Small list issued {small} commands.");
        Assert.True(large <= 16 && large <= small + 8,
            $"List commands must grow by batches, not entries: small={small}, large={large}.");
    }

    [PostgresFact]
    public async Task ListAsync_BoundsPostgresCommandsForLargeEvidenceBatch()
    {
        var commands = await MeasureListCommandsAsync(300, postgres: true);
        _output.WriteLine($"PostgreSQL ListAsync commands: 300 distinct tracks = {commands} (four entries per track).");
        Assert.True(commands <= 16, $"Large PostgreSQL list issued {commands} commands.");
    }

    private static async Task<int> MeasureListCommandsAsync(int trackCount, bool postgres = false)
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: !postgres, postgres: postgres);
        scope.Playlist.AllowDuplicateTracks = true;
        var progress = new Dictionary<Guid, PlaylistMatchingResult>();
        var expected = new List<Guid> { scope.Entry.Id };
        var ownedPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Owned observations" };
        scope.Db.Add(ownedPlaylist);
        for (var index = 0; index < trackCount; index++)
        {
            var track = new Track { Id = Guid.NewGuid(), SearchTitle = $"Track {index}",
                CanonicalMetadata = JsonSerializer.Serialize(new TrackCanonicalMetadata { Title = $"Track {index}" }) };
            scope.Db.Add(track);
            // A canonical provider key alone never authorizes a destination identity.
            scope.Db.Add(new TrackSourceId { Id = Guid.NewGuid(), Track = track, TrackId = track.Id,
                SourceType = "spotify", ExternalId = $"private-{index}" });
            var entries = Enumerable.Range(0, 4).Select(offset => new PlaylistEntry
            {
                Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id, Track = track, TrackId = track.Id,
                Position = 1 + index * 4 + offset
            }).ToArray();
            scope.Db.AddRange(entries);
            progress[entries[0].Id] = PlaylistMatchingProgress.Create(scope.Link, entries[0], $"manual-{index}", true);
            progress[entries[1].Id] = PlaylistMatchingProgress.Create(scope.Link, entries[1], $"checkpoint-{index}");
            progress[entries[2].Id] = PlaylistMatchingProgress.Create(scope.Link, entries[0], $"wrong-entry-{index}", true);
            progress[entries[3].Id] = PlaylistMatchingProgress.Create(scope.Link, entries[3], $"reassigned-{index}", true);
            scope.Db.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = scope.Entry.TrackId!.Value,
                SourceType = "spotify", ExternalId = $"reassigned-{index}" });
            if (index % 5 == 0)
            {
                var observation = new TrackObservation { Id = Guid.NewGuid(), TrackId = track.Id,
                    SourceType = "spotify", ExternalId = $"owned-{index}", Title = "Owned", MatchStatus = "matched" };
                scope.Db.Add(new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = ownedPlaylist.Id,
                    TrackId = track.Id, TrackObservation = observation, TrackObservationId = observation.Id, Position = index });
            }
            else expected.AddRange(entries.Skip(2).Select(entry => entry.Id));
        }
        scope.Link.MatchingProgressJson = PlaylistMatchingProgress.Serialize(progress);
        await scope.Db.SaveChangesAsync();
        var counter = new ListCommandCounter();
        await using var db = scope.NewContext(counter);
        var actual = await scope.CreateReview(db).ListAsync(scope.UserId, scope.Playlist.Id, scope.Link.Id, default);
        Assert.Equal(expected, actual.Select(entry => entry.EntryId));
        return counter.Count;
    }

    [Fact]
    public async Task BatchEvidence_PreservesPriorityEntryScopeAndRequesterOwnership()
    {
        await using var scope = await Scope.CreateAsync("spotify", relational: true);
        scope.Playlist.AllowDuplicateTracks = true;
        var otherUser = TestUserFactory.Create(scope.UserId + 1, "batch-other@example.com");
        var otherPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = otherUser.Id, Name = "Private" };
        var ownedPlaylist = new Playlist { Id = Guid.NewGuid(), UserId = scope.UserId, Name = "Owned" };
        scope.Db.AddRange(otherUser, otherPlaylist, ownedPlaylist);
        var entries = new List<PlaylistEntry> { scope.Entry };
        PlaylistEntry AddEntry(Track? track = null)
        {
            track ??= new Track { Id = Guid.NewGuid() };
            var entry = new PlaylistEntry { Id = Guid.NewGuid(), Track = track, TrackId = track.Id,
                PlaylistId = scope.Playlist.Id, Position = entries.Count };
            scope.Db.Add(entry);
            entries.Add(entry);
            return entry;
        }
        void Attach(PlaylistEntry entry, string key, Guid? observationTrackId)
        {
            entry.TrackObservation = new TrackObservation { Id = Guid.NewGuid(), TrackId = observationTrackId,
                SourceType = "spotify", ExternalId = key, Title = key, MatchStatus = "matched" };
            entry.TrackObservationId = entry.TrackObservation.Id;
            scope.Db.Add(entry.TrackObservation);
        }
        void AddObserved(Playlist playlist, Track track, string key, int position)
        {
            var entry = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = playlist.Id,
                TrackId = track.Id, Position = position };
            Attach(entry, key, track.Id);
            scope.Db.Add(entry);
        }
        var manualA = AddEntry();
        var manualB = AddEntry(manualA.Track);
        var owned = AddEntry(manualA.Track);
        var automaticWithOwned = AddEntry(manualA.Track);
        var attached = AddEntry(manualA.Track);
        Attach(attached, "z-attached", attached.TrackId);
        AddObserved(ownedPlaylist, manualA.Track!, "owned-z", 0);
        AddObserved(ownedPlaylist, manualA.Track!, "owned-a", 1);
        var manualWithAttached = AddEntry();
        Attach(manualWithAttached, "attached-alternative", manualWithAttached.TrackId);
        var checkpoint = AddEntry();
        var privateOnly = AddEntry();
        AddObserved(otherPlaylist, privateOnly.Track!, "other-users-observation", 0);
        scope.Db.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = privateOnly.TrackId!.Value,
            SourceType = "spotify", ExternalId = "global-private" });
        var inconsistent = AddEntry();
        Attach(inconsistent, "inconsistent", privateOnly.TrackId);
        var reassigned = AddEntry();
        scope.Db.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = privateOnly.TrackId.Value,
            SourceType = "spotify", ExternalId = "reassigned" });
        var wrongScope = AddEntry();
        var staleMetadata = AddEntry();
        var legacy = AddEntry();
        var unresolved = new PlaylistEntry { Id = Guid.NewGuid(), PlaylistId = scope.Playlist.Id,
            Position = entries.Count };
        Attach(unresolved, "unresolved-direct", null);
        scope.Db.Add(unresolved);
        entries.Add(unresolved);
        await scope.Db.SaveChangesAsync();
        var proofs = new Dictionary<Guid, PlaylistMatchingResult>
        {
            [manualA.Id] = PlaylistMatchingProgress.Create(scope.Link, manualA, "manual-a", true),
            [manualB.Id] = PlaylistMatchingProgress.Create(scope.Link, manualB, "manual-b", true),
            [automaticWithOwned.Id] = PlaylistMatchingProgress.Create(scope.Link, automaticWithOwned, "automatic"),
            [manualWithAttached.Id] = PlaylistMatchingProgress.Create(scope.Link, manualWithAttached, "manual-attached", true),
            [checkpoint.Id] = PlaylistMatchingProgress.Create(scope.Link, checkpoint, "checkpoint"),
            [reassigned.Id] = PlaylistMatchingProgress.Create(scope.Link, reassigned, "reassigned", true),
            [wrongScope.Id] = PlaylistMatchingProgress.Create(scope.Link, privateOnly, "wrong-scope", true),
            [staleMetadata.Id] = PlaylistMatchingProgress.Create(scope.Link, staleMetadata, "stale", true) with { Fingerprint = "stale" },
            [legacy.Id] = PlaylistMatchingProgress.Create(scope.Link, legacy, "legacy", true) with { ProofVersion = 1 }
        };
        scope.Link.MatchingProgressJson = PlaylistMatchingProgress.Serialize(proofs);
        await scope.Db.SaveChangesAsync();
        await using var db = scope.NewContext();
        var loaded = await db.PlaylistEntries.AsNoTracking().Where(entry => entry.PlaylistId == scope.Playlist.Id)
            .Include(entry => entry.Track).ThenInclude(track => track!.SourceIds).Include(entry => entry.TrackObservation)
            .OrderBy(entry => entry.Position).ToListAsync();
        var link = await db.ServicePlaylistMappings.AsNoTracking().Include(item => item.ConnectedServiceAccount).SingleAsync();
        var account = new PlatformAccountContext(scope.UserId, link.ConnectedServiceAccountId, link.ExternalAccountId);
        var batch = await PlaylistDestinationIdentityEvidence.FindManyAsync(db, account, "spotify", loaded, link, default);
        var expected = new Dictionary<Guid, string>
        {
            [manualA.Id] = "manual-a", [manualB.Id] = "manual-b", [owned.Id] = "owned-a",
            [automaticWithOwned.Id] = "owned-a", [attached.Id] = "z-attached",
            [manualWithAttached.Id] = "manual-attached", [checkpoint.Id] = "checkpoint",
            [unresolved.Id] = "unresolved-direct"
        };
        Assert.Equal(expected.OrderBy(item => item.Key), batch.OrderBy(item => item.Key));
        foreach (var entry in loaded)
        {
            Assert.Equal(expected.GetValueOrDefault(entry.Id),
                await PlaylistDestinationIdentityEvidence.FindAsync(db, account, "spotify", entry, default, link));
            // The resolver also discovers its link rather than receiving one.
            Assert.Equal(expected.GetValueOrDefault(entry.Id),
                await PlaylistDestinationIdentityEvidence.FindAsync(db, account, "spotify", entry, default));
        }
        var expectedUnmatched = loaded.Where(entry => !expected.ContainsKey(entry.Id)).Select(entry => entry.Id);
        var review = scope.CreateReview(db);
        Assert.Equal(expectedUnmatched, (await review.ListAsync(
            scope.UserId, scope.Playlist.Id, link.Id, default)).Select(entry => entry.EntryId));

        // A later list must read fresh reassignment evidence, even on the same service instance.
        scope.Db.Add(new TrackSourceId { Id = Guid.NewGuid(), TrackId = privateOnly.TrackId.Value,
            SourceType = "spotify", ExternalId = "checkpoint" });
        await scope.Db.SaveChangesAsync();
        Assert.Contains(await review.ListAsync(scope.UserId, scope.Playlist.Id, link.Id, default),
            entry => entry.EntryId == checkpoint.Id);
    }

    private sealed class ListCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Count++;
            return result;
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Count++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
