using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>Associates independently verified provider facts with recordings, never with playlist authority.</summary>
internal sealed class ProviderRecordingAssociations(ApplicationDbContext db, TrackMatchingOptions? options = null,
    TimeProvider? clock = null)
{
    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    public async Task<ProviderCatalogItem?> LoadCandidateAsync(TrackMatchSearchCandidate candidate, CancellationToken ct)
    {
        if (candidate.ProviderCatalogItemId is not { } id || candidate.ProviderCatalogRevision is null) return null;
        var item = await db.ProviderCatalogItems.SingleOrDefaultAsync(row => row.Id == id, ct);
        if (item is not null && db.Entry(item).State != EntityState.Added)
            await db.Entry(item).ReloadAsync(ct);
        return item is not null && db.Entry(item).State != EntityState.Detached
            && item.SourceType == candidate.CandidateSource && item.ExternalId == candidate.ExternalId
            && item.Revision == candidate.ProviderCatalogRevision && ProviderCatalogPolicy.ReadFacts(item, Now) is not null
            ? item : null;
    }

    public async Task<TrackSourceId?> FindUsableAsync(string source, string externalId, CancellationToken ct)
    {
        var mapping = await db.TrackSourceIds.AsNoTracking().Include(row => row.ProviderCatalogItem)
            .SingleOrDefaultAsync(row => row.SourceType == source && row.ExternalId == externalId, ct);
        return ProviderCatalogPolicy.IsVerified(mapping, Now) ? mapping : null;
    }

    public async Task<Track?> RevalidateExactAsync(ProviderCatalogItem item, CancellationToken ct)
    {
        var mapping = await db.TrackSourceIds.SingleOrDefaultAsync(row => row.SourceType == item.SourceType
            && row.ExternalId == item.ExternalId, ct);
        if (mapping is not { AssociationState: RecordingAssociationState.Verified, IndependentEvidenceJson: not null }
            || mapping.AssociationMethod is not ("provider_origin" or "independent_recording")) return null;
        var track = await db.Tracks.Include(row => row.SourceIds).ThenInclude(row => row.ProviderCatalogItem)
            .SingleAsync(row => row.Id == mapping.TrackId, ct);
        return await TryAssociateAsync(track, item, null, false, ct) ? track : null;
    }

    public async Task<bool> TryAssociateCandidateAsync(Track track, TrackMatchSearchCandidate candidate,
        ProviderCatalogFacts? independentFacts, bool providerOrigin, CancellationToken ct)
    {
        var item = await LoadCandidateAsync(candidate, ct);
        return item is not null && await TryAssociateAsync(track, item, independentFacts, providerOrigin, ct);
    }

    // Callers capture independentFacts before search/acquisition can change a mapping.
    // providerOrigin is reserved for a recording originating from an actual verified
    // provider response. Manual choices never use that path.
    public async Task<bool> TryAssociateAsync(Track track, ProviderCatalogItem item,
        ProviderCatalogFacts? independentFacts, bool providerOrigin, CancellationToken ct)
    {
        var facts = ProviderCatalogPolicy.ReadFacts(item, Now);
        if (facts is null) return false;
        var mapping = db.TrackSourceIds.Local.FirstOrDefault(row => row.SourceType == item.SourceType && row.ExternalId == item.ExternalId)
            ?? await db.TrackSourceIds.Include(row => row.ProviderCatalogItem)
                .SingleOrDefaultAsync(row => row.SourceType == item.SourceType && row.ExternalId == item.ExternalId, ct);
        if (mapping?.AssociationState == RecordingAssociationState.Conflicted) return false;

        // A prior association's stored evidence may revalidate that same provider key.
        // Caller-supplied candidates and legacy confidence/origin JSON cannot do so.
        var retainedOrigin = false;
        if (independentFacts is null && mapping is { AssociationState: RecordingAssociationState.Verified,
                IndependentEvidenceJson: not null }
            && mapping.AssociationMethod is "provider_origin" or "independent_recording")
        {
            try { independentFacts = JsonSerializer.Deserialize<ProviderCatalogFacts>(mapping.IndependentEvidenceJson);
                retainedOrigin = mapping.AssociationMethod == "provider_origin"; }
            catch (JsonException) { return false; }
        }
        var otherPublicAssociation = track.SourceIds.Any(row => row.SourceType != item.SourceType
            && ProviderCatalogPolicy.IsVerified(row, Now));
        if (independentFacts is null)
        {
            if (!providerOrigin || otherPublicAssociation) return false;
        }
        else if (!Corroborates(independentFacts, item, facts, retainedOrigin)) return false;

        // The revision participates in the same save transaction as the mapping.
        // A concurrent facts change or withdrawal rejects this stale assertion.
        if (db.Entry(item).State != EntityState.Added)
            db.Entry(item).Property(row => row.Revision).IsModified = true;

        if (mapping is not null && mapping.TrackId != track.Id)
        {
            // Only corroborated evidence may quarantine an existing association.
            // A caller holding a catalog reference cannot invalidate it by itself.
            if (independentFacts is not null)
            {
                mapping.AssociationState = RecordingAssociationState.Conflicted;
                mapping.Revision++;
            }
            return false;
        }

        if (mapping is null)
        {
            mapping = new TrackSourceId { Id = Guid.NewGuid(), TrackId = track.Id, Track = track,
                SourceType = item.SourceType, ExternalId = item.ExternalId };
            db.TrackSourceIds.Add(mapping);
        }
        mapping.ProviderCatalogItem = item;
        mapping.ProviderCatalogItemId = item.Id;
        mapping.CatalogRevision = item.Revision;
        mapping.AssociationState = RecordingAssociationState.Verified;
        mapping.AssociationMethod = providerOrigin && independentFacts is null || retainedOrigin ? "provider_origin" : "independent_recording";
        mapping.AssociationVerifiedAt = Now;
        mapping.IndependentEvidenceJson = JsonSerializer.Serialize(independentFacts ?? facts);
        mapping.Revision++;
        mapping.LastVerifiedAt = Now;
        mapping.Confidence = 1m;
        mapping.IsOfficial = true;

        track.PreferredCatalogItem = item;
        track.PreferredCatalogItemId = item.Id;
        var title = facts.Title.Trim();
        var artist = facts.Artist?.Trim();
        track.SearchTitle = title[..Math.Min(title.Length, 512)];
        track.SearchArtist = string.IsNullOrEmpty(artist) ? null : artist[..Math.Min(artist.Length, 1024)];
        track.Isrc = TrackIdentityResolver.NormalizeIsrc(facts.Isrc);
        track.MbidRecording = facts.MusicBrainzRecordingId;
        track.VersionFlags = facts.VersionFlags;
        if (facts.ArtistCredits.Count > 0)
        {
            await new ArtistReconciliationService(db).ReconcileAsync(track, facts.ArtistCredits,
                item.SourceType, Now, ct, facts.Artist);
            foreach (var credit in db.TrackArtistCredits.Local.Where(row => row.TrackId == track.Id
                && facts.ArtistCredits.Select((verifiedCredit, position) => (verifiedCredit, position))
                    .Any(verified => verified.position == row.Position
                        && verified.verifiedCredit.Name == row.CreditedName
                        && (verified.verifiedCredit.Role ?? TrackArtistRole.Primary) == row.Role)))
            {
                credit.ProviderCatalogItemId = item.Id;
                credit.ProviderCatalogItem = item;
            }
            foreach (var link in db.ArtistProviderLinks.Local.Where(row => row.Provider == item.SourceType
                         && facts.ArtistCredits.Any(credit => credit.ExternalId == row.ExternalId)))
            {
                var publicCredit = facts.ArtistCredits.First(credit => credit.ExternalId == link.ExternalId);
                link.ProfileUrl = publicCredit.ProfileUrl;
                link.ArtworkUrl = publicCredit.ArtworkUrl;
                link.RawMetadata = null;
                link.VerificationMethod = item.VerificationMethod;
                link.VerifiedAt = item.VerifiedAt;
                link.RevalidateAfter = item.RevalidateAfter;
            }
        }
        return true;
    }

    private bool Corroborates(ProviderCatalogFacts independent, ProviderCatalogItem item, ProviderCatalogFacts proposed,
        bool retainedOrigin)
    {
        var isrc = TrackIdentityResolver.NormalizeIsrc(independent.Isrc);
        var proposedIsrc = TrackIdentityResolver.NormalizeIsrc(proposed.Isrc);
        var mbid = Normalize(independent.MusicBrainzRecordingId);
        var proposedMbid = Normalize(proposed.MusicBrainzRecordingId);
        if (isrc is not null && proposedIsrc is not null && isrc != proposedIsrc
            || mbid is not null && proposedMbid is not null && mbid != proposedMbid) return false;
        if (!retainedOrigin && !(isrc is not null && isrc == proposedIsrc || mbid is not null && mbid == proposedMbid)) return false;
        if (independent.VersionFlags != proposed.VersionFlags) return false;
        var evidence = new TrackObservation { Id = Guid.NewGuid(), SourceType = "catalog", ExternalId = "independent",
            Title = independent.Title, Artist = independent.Artist, DurationSeconds = independent.DurationSeconds,
            RawMetadata = JsonSerializer.Serialize(new TrackObservationMetadata { Title = independent.Title, Artist = independent.Artist,
                Isrc = independent.Isrc, ArtistIdentities = independent.ArtistCredits }), MatchStatus = TrackMatchingStatuses.Pending };
        return TrackMatchDecisionEngine.Evaluate(evidence, [ProviderCatalogPolicy.Candidate(item, proposed)],
            options ?? new TrackMatchingOptions()).Decision.AcceptedCandidate is not null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
