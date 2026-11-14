using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>
/// Reconciles stable identities globally. Name evidence may only enrich a credit
/// on the same track; it never merges independent same-name artists.
/// </summary>
public sealed class ArtistReconciliationService(ApplicationDbContext dbContext)
{
    public async Task ReconcileAsync(
        Track track, IReadOnlyList<ArtistCreditMetadata> credits, string source,
        DateTimeOffset now, CancellationToken cancellationToken, string? displayArtist = null,
        Guid? sourceItemId = null, long? sourceRevision = null)
    {
        if (sourceItemId.HasValue != sourceRevision.HasValue || sourceRevision is <= 0)
            throw new ArgumentException("Artist source evidence requires both an item ID and a positive revision.");

        if (dbContext.Entry(track).State != EntityState.Added)
        {
            await dbContext.Entry(track).Collection(t => t.ArtistCredits).Query()
                .Include(c => c.Artist).ThenInclude(a => a!.ProviderLinks).LoadAsync(cancellationToken);
        }

        var retry = dbContext.ArtistReconciliationRetry;
        retry.Record(track, credits, source, now, displayArtist, sourceItemId, sourceRevision);
        retry.CaptureTrack(track);

        // Parse only the legacy anonymous display. The provider's structured list
        // remains authoritative; never infer separate identities from its names.
        if (credits.Count > 1 && credits.All(c => !string.IsNullOrWhiteSpace(c.Name))
            && track.ArtistCredits.Count == 1
            && track.ArtistCredits.Single() is { } aggregate
            && !credits.Any(c => Normalize(c.Name) == Normalize(aggregate.CreditedName))
            && aggregate.Artist is { } anonymous)
        {
            await LoadLinksAsync(anonymous, cancellationToken);
            if (anonymous.ProviderLinks.Count == 0)
            {
                var legacyNames = TrackMetadataParser.Parse(null, aggregate.CreditedName, false).ArtistCredits;
                var incomingNames = TrackMetadataParser.NormalizeArtistCredits(credits.Select(c => c.Name));
                if (legacyNames.Count != credits.Count || !legacyNames.SequenceEqual(incomingNames, StringComparer.Ordinal))
                {
                    for (var index = 0; index < credits.Count; index++)
                        await ConflictAsync(track, credits[index], source, index,
                            "Incoming collaboration cannot safely replace the legacy aggregate credit.", now, cancellationToken, sourceItemId, sourceRevision);
                    return;
                }
                track.ArtistCredits.Remove(aggregate);
                dbContext.TrackArtistCredits.Remove(aggregate);
            }
        }

        for (var position = 0; position < credits.Count; position++)
        {
            var incoming = credits[position];
            var role = incoming.Role ?? TrackArtistRole.Primary;
            if (string.IsNullOrWhiteSpace(incoming.Name)) continue;
            var provider = Clean(incoming.Provider)?.ToLowerInvariant();
            var externalId = Clean(incoming.ExternalId);
            var mbid = Clean(incoming.MusicBrainzArtistId)?.ToLowerInvariant();
            if (provider == "musicbrainz")
            {
                externalId = externalId?.ToLowerInvariant() ?? mbid;
                mbid ??= externalId;
            }
            if (mbid != null && provider == null)
            {
                provider = "musicbrainz";
                externalId = mbid;
            }

            if (provider == "musicbrainz" && externalId != null && mbid != externalId)
            {
                await ConflictAsync(track, incoming, source, position, "MusicBrainz provider ID disagrees with the supplied MusicBrainz artist ID.", now, cancellationToken, sourceItemId, sourceRevision);
                continue;
            }

            var mbLink = mbid == null ? null : await FindLinkAsync("musicbrainz", mbid, cancellationToken);
            var byMbid = mbLink?.Artist;
            var link = provider == null || externalId == null ? null
                : await FindLinkAsync(provider, externalId, cancellationToken);
            var byProvider = link?.Artist;
            if (byMbid != null && byProvider != null && byMbid.Id != byProvider.Id)
            {
                await ConflictAsync(track, incoming, source, position, "Provider ID and MusicBrainz ID identify different artists.", now, cancellationToken, sourceItemId, sourceRevision);
                continue;
            }
            var stableArtist = byMbid ?? byProvider;
            var existing = stableArtist == null ? null : track.ArtistCredits.FirstOrDefault(c => c.ArtistId == stableArtist.Id && (incoming.Role == null || c.Role == role || c.Position == position));
            var named = track.ArtistCredits.Where(c => (incoming.Role == null || c.Role == role || c.Position == position) && Normalize(c.CreditedName) == Normalize(incoming.Name)).ToArray();
            existing ??= named.FirstOrDefault(c => c.Position == position);
            if (existing == null && named.Length == 1
                && credits.Count(c => Normalize(c.Name) == Normalize(incoming.Name)) == 1)
                existing = named[0];
            var current = existing?.Artist;
            if (current == null && existing != null)
                current = await dbContext.Artists.FindAsync([existing.ArtistId], cancellationToken);
            if (current != null) await LoadLinksAsync(current, cancellationToken);
            var separateIdentities = current != null && stableArtist != null && current.Id != stableArtist.Id
                && current.ProviderLinks.Count > 0;
            var artist = stableArtist ?? current;
            if (artist != null)
            {
                await LoadLinksAsync(artist, cancellationToken);
                var conflictingMbid = mbid != null && artist.ProviderLinks.Any(l => l.Provider == "musicbrainz" && l.ExternalId != mbid);
                var conflictingProvider = provider != null && externalId != null && artist.ProviderLinks.Any(l => l.Provider == provider && l.ExternalId != externalId);
                if (conflictingMbid || conflictingProvider || separateIdentities)
                {
                    await ConflictAsync(track, incoming, source, position, "Incoming identity conflicts with an existing artist credit.", now, cancellationToken, sourceItemId, sourceRevision);
                    continue;
                }
            }
            // Keep canonical order stable and retain the source's complete ordering
            // evidence. In particular, do not attach its join phrase to another slot.
            if (existing != null && existing.Position != position)
            {
                await ConflictAsync(track, incoming, source, position,
                    "Incoming credit order disagrees with the existing credit order.", now, cancellationToken, sourceItemId, sourceRevision);
                continue;
            }
            if (existing == null && track.ArtistCredits.Any(c => c.Position == position))
            {
                await ConflictAsync(track, incoming, source, position, "Incoming credit disagrees with an occupied credit position.", now, cancellationToken, sourceItemId, sourceRevision);
                continue;
            }
            if (artist == null)
            {
                artist = new Artist { Id = Guid.NewGuid(), Name = Clean(incoming.CanonicalName) ?? incoming.Name.Trim(), CreatedAt = now, UpdatedAt = now };
                retry.Capture(artist);
                dbContext.Artists.Add(artist);
            }
            if (string.IsNullOrWhiteSpace(artist.SortName)) artist.SortName = Clean(incoming.SortName);
            artist.UpdatedAt = now;
            if (provider != null && externalId != null)
                UpdateLink(artist, link, provider, externalId, incoming, now);
            // A Spotify credit can independently supply a MusicBrainz identity.
            // Persist both identities on the same artist, including during replay.
            if (mbid != null && provider != "musicbrainz")
                UpdateLink(artist, mbLink, "musicbrainz", mbid, null, now);
            if (existing == null)
            {
                existing = new TrackArtistCredit { Id = Guid.NewGuid(), TrackId = track.Id, ArtistId = artist.Id,
                    Artist = artist, Position = position, Role = role, CreditedName = incoming.Name.Trim() };
                retry.Capture(existing);
                track.ArtistCredits.Add(existing);
                dbContext.TrackArtistCredits.Add(existing);
            }
            existing.Artist = artist;
            existing.ArtistId = artist.Id;
            if (incoming.Role != null) existing.Role = role;
            existing.JoinPhrase ??= incoming.JoinPhrase;
            if (sourceItemId != null)
            {
                existing.ProviderCatalogItemId = sourceItemId;
                existing.ProviderCatalogRevision = sourceRevision;
                existing.SourceCreditPosition = position;
            }
        }
    }

    private async Task<ArtistProviderLink?> FindLinkAsync(string provider, string externalId, CancellationToken cancellationToken)
    {
        var link = dbContext.ArtistProviderLinks.Local.FirstOrDefault(l => l.Provider == provider && l.ExternalId == externalId)
            ?? await dbContext.ArtistProviderLinks.Include(l => l.Artist).FirstOrDefaultAsync(
                l => l.Provider == provider && l.ExternalId == externalId, cancellationToken);
        if (link != null && link.Artist == null)
            link.Artist = await dbContext.Artists.FindAsync([link.ArtistId], cancellationToken);
        return link;
    }

    private void UpdateLink(Artist artist, ArtistProviderLink? link, string provider, string externalId,
        ArtistCreditMetadata? incoming, DateTimeOffset now)
    {
        link ??= new ArtistProviderLink { Id = Guid.NewGuid(), ArtistId = artist.Id, Artist = artist,
            Provider = provider, ExternalId = externalId };
        dbContext.ArtistReconciliationRetry.Capture(link);
        link.ProfileUrl = Clean(incoming?.ProfileUrl) ?? link.ProfileUrl
            ?? (provider == "musicbrainz" ? $"https://musicbrainz.org/artist/{externalId}" : null);
        link.ArtworkUrl = Clean(incoming?.ArtworkUrl) ?? link.ArtworkUrl;
        link.RawMetadata = incoming?.RawMetadata ?? link.RawMetadata;
        link.UpdatedAt = now;
        if (dbContext.Entry(link).State == EntityState.Detached) dbContext.ArtistProviderLinks.Add(link);
    }

    private async Task LoadLinksAsync(Artist artist, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(artist).State != EntityState.Added)
            await dbContext.Entry(artist).Collection(a => a.ProviderLinks).LoadAsync(cancellationToken);
        dbContext.ArtistReconciliationRetry.Capture(artist);
        foreach (var link in artist.ProviderLinks) dbContext.ArtistReconciliationRetry.Capture(link);
    }

    private async Task ConflictAsync(Track track, ArtistCreditMetadata incoming, string source, int position,
        string reason, DateTimeOffset now, CancellationToken cancellationToken, Guid? sourceItemId, long? sourceRevision)
    {
        var identities = JsonSerializer.Serialize(new {
            Provider = Clean(incoming.Provider)?.ToLowerInvariant(),
            ExternalId = string.Equals(Clean(incoming.Provider), "musicbrainz", StringComparison.OrdinalIgnoreCase)
                ? Clean(incoming.ExternalId)?.ToLowerInvariant() : Clean(incoming.ExternalId),
            MusicBrainzArtistId = Clean(incoming.MusicBrainzArtistId)?.ToLowerInvariant(),
            existing = track.ArtistCredits.OrderBy(c => c.Position).Select(c => new {
                c.ArtistId, providers = c.Artist?.ProviderLinks.OrderBy(l => l.Provider).ThenBy(l => l.ExternalId)
                    .Select(l => new { l.Provider, l.ExternalId }) }) });
        var evidence = JsonSerializer.Serialize(new { source, sourceItemId, sourceRevision, position, reason, identities });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        if (dbContext.ArtistReconciliationConflicts.Local.Any(c => c.TrackId == track.Id && c.EvidenceKey == key)
            || await dbContext.ArtistReconciliationConflicts.AnyAsync(c => c.TrackId == track.Id && c.EvidenceKey == key, cancellationToken)) return;
        var conflict = new ArtistReconciliationConflict {
            Id = Guid.NewGuid(), TrackId = track.Id, EvidenceKey = key, IdentityConflictJson = identities,
            ProviderCatalogItemId = sourceItemId, ProviderCatalogRevision = sourceRevision,
            SourceCreditPosition = position, Reason = reason, CreatedAt = now };
        dbContext.ArtistReconciliationRetry.Capture(conflict);
        dbContext.ArtistReconciliationConflicts.Add(conflict);
    }

    private static string Normalize(string? value) => TrackTextNormalizer.Normalize(value);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
