using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace Cantaro.Api.Services;

/// <summary>
/// Retains only artist reconciliation's edits until the caller saves its import.
/// After an identity or evidence race, replay those edits against committed rows
/// without discarding playlist, observation, or track changes in the same unit of work.
/// </summary>
internal sealed class ArtistReconciliationRetryState(ApplicationDbContext db)
{
    private readonly Dictionary<object, Snapshot> _before = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Track, TrackArtistCredit[]> _tracks = [];
    private readonly List<Request> _requests = [];
    private bool _replaying;

    public bool CanRetry(DbUpdateException exception) => _requests.Count > 0
        && db.Database.AutoTransactionBehavior != Microsoft.EntityFrameworkCore.AutoTransactionBehavior.Never
        && (db.Database.CurrentTransaction == null || db.Database.AutoSavepointsEnabled)
        && exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_ArtistProviderLinks_Provider_ExternalId"
                or "IX_ArtistProviderLinks_ArtistId_Provider" or "IX_Artists_MusicBrainzArtistId"
                or "IX_ArtistReconciliationConflicts_TrackId_EvidenceKey"
        };

    public void Record(Track track, IReadOnlyList<ArtistCreditMetadata> credits, string source,
        DateTimeOffset now, string? displayArtist)
    {
        if (!_replaying) _requests.Add(new(track, credits.ToArray(), source, now, displayArtist));
    }

    public void CaptureTrack(Track track)
    {
        _tracks.TryAdd(track, track.ArtistCredits.ToArray());
        foreach (var credit in track.ArtistCredits)
        {
            Capture(credit);
            if (credit.Artist is not { } artist) continue;
            Capture(artist);
            foreach (var link in artist.ProviderLinks) Capture(link);
        }
    }

    public void Capture(object entity)
    {
        if (_before.ContainsKey(entity)) return;
        var entry = db.Entry(entity);
        _before.Add(entity, new(entry.State, entry.CurrentValues.Clone(), entry.OriginalValues.Clone(),
            entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToHashSet(),
            (entity as TrackArtistCredit)?.Artist,
            (entity as Artist)?.ProviderLinks.ToArray(), (entity as Artist)?.TrackCredits.ToArray()));
    }

    public void Clear()
    {
        _before.Clear();
        _tracks.Clear();
        _requests.Clear();
    }

    public async Task ReplayAsync(CancellationToken cancellationToken)
    {
        // EF has rolled back the failed SaveChanges transaction (or savepoint).
        // Restore the pre-reconciliation graph as well as EF's scalar state, so a
        // losing anonymous artist promotion is re-evaluated rather than force-merged.
        var detectChanges = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            // Detach tentative conflicts too. Replay checks their evidence keys
            // against the winning rows without replacing the caller's other edits.
            foreach (var (entity, snapshot) in _before)
                if (snapshot.State == EntityState.Detached && entity is not Artist)
                    db.Entry(entity).State = EntityState.Detached;
            foreach (var (entity, snapshot) in _before)
            {
                if (snapshot.State == EntityState.Detached) continue;
                var entry = db.Entry(entity);
                entry.CurrentValues.SetValues(snapshot.Values);
                entry.OriginalValues.SetValues(snapshot.OriginalValues);
                entry.State = snapshot.State;
                foreach (var property in entry.Properties)
                    property.IsModified = snapshot.Modified.Contains(property.Metadata.Name);
                if (entity is TrackArtistCredit credit) credit.Artist = snapshot.Artist;
                if (entity is Artist artist && snapshot.Links != null)
                {
                    artist.ProviderLinks = snapshot.Links.ToList();
                    artist.TrackCredits = snapshot.Credits!.ToList();
                    entry.Collection(nameof(Artist.ProviderLinks)).IsLoaded = false;
                }
            }
            foreach (var (entity, snapshot) in _before)
                if (snapshot.State == EntityState.Detached && entity is Artist)
                    db.Entry(entity).State = EntityState.Detached;
            foreach (var (track, credits) in _tracks) track.ArtistCredits = credits.ToList();
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detectChanges; }
        db.ChangeTracker.DetectChanges();
        _before.Clear();
        _tracks.Clear();

        _replaying = true;
        try
        {
            foreach (var request in _requests)
            {
                if (db.Entry(request.Track).State == EntityState.Detached) continue;
                await new ArtistReconciliationService(db).ReconcileAsync(request.Track, request.Credits,
                    request.Source, request.Now, cancellationToken, request.DisplayArtist);
            }
        }
        finally { _replaying = false; }
    }

    private sealed record Request(Track Track, IReadOnlyList<ArtistCreditMetadata> Credits,
        string Source, DateTimeOffset Now, string? DisplayArtist);
    private sealed record Snapshot(EntityState State, PropertyValues Values, PropertyValues OriginalValues,
        HashSet<string> Modified, Artist? Artist, ArtistProviderLink[]? Links, TrackArtistCredit[]? Credits);
}
