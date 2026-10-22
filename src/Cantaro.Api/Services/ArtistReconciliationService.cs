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
        DateTimeOffset now, CancellationToken cancellationToken, string? displayArtist = null)
    {
        if (dbContext.Entry(track).State != EntityState.Added)
        {
            await dbContext.Entry(track).Collection(t => t.ArtistCredits).Query()
                .Include(c => c.Artist).ThenInclude(a => a!.ProviderLinks).LoadAsync(cancellationToken);
        }

        // Older imports stored the whole collaboration as a single anonymous
        // artist. Replace only that exact display credit, never an identified one.
        if (credits.Count > 1 && credits.All(c => !string.IsNullOrWhiteSpace(c.Name))
            && track.ArtistCredits.Count == 1
            && track.ArtistCredits.Single() is { } aggregate
            && Normalize(aggregate.CreditedName) == Normalize(displayArtist)
            && aggregate.Artist is { MusicBrainzArtistId: null } anonymous)
        {
            await LoadLinksAsync(anonymous, cancellationToken);
            if (anonymous.ProviderLinks.Count == 0)
            {
                track.ArtistCredits.Remove(aggregate);
                dbContext.TrackArtistCredits.Remove(aggregate);
            }
        }

        for (var position = 0; position < credits.Count; position++)
        {
            var incoming = credits[position];
            var role = incoming.Role ?? TrackArtistRole.Primary;
            if (string.IsNullOrWhiteSpace(incoming.Name)) continue;
            var provider = incoming.Provider?.Trim().ToLowerInvariant();
            var externalId = Clean(incoming.ExternalId);
            var mbid = Clean(incoming.MusicBrainzArtistId)?.ToLowerInvariant();
            if (provider == "musicbrainz")
            {
                externalId = externalId?.ToLowerInvariant();
                mbid ??= externalId;
            }
            if (mbid != null && provider == null)
            {
                provider = "musicbrainz";
                externalId = mbid;
            }

            if (provider == "musicbrainz" && externalId != null && mbid != externalId)
            {
                await ConflictAsync(track, incoming, source, position, "MusicBrainz provider ID disagrees with the supplied MusicBrainz artist ID.", now, cancellationToken);
                continue;
            }

            var byMbid = mbid == null ? null : dbContext.Artists.Local.FirstOrDefault(a => string.Equals(a.MusicBrainzArtistId, mbid, StringComparison.OrdinalIgnoreCase))
                ?? await dbContext.Artists.FirstOrDefaultAsync(a => a.MusicBrainzArtistId == mbid, cancellationToken);
            var link = provider == null || externalId == null ? null
                : dbContext.ArtistProviderLinks.Local.FirstOrDefault(l => l.Provider == provider && l.ExternalId == externalId)
                ?? await dbContext.ArtistProviderLinks.Include(l => l.Artist).FirstOrDefaultAsync(
                    l => l.Provider == provider && l.ExternalId == externalId, cancellationToken);
            var byProvider = link?.Artist ?? (link == null ? null : await dbContext.Artists.FindAsync([link.ArtistId], cancellationToken));
            if (byMbid != null && byProvider != null && byMbid.Id != byProvider.Id)
            {
                await ConflictAsync(track, incoming, source, position, "Provider ID and MusicBrainz ID identify different artists.", now, cancellationToken);
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
                && (current.MusicBrainzArtistId != null || current.ProviderLinks.Count > 0);
            var artist = stableArtist ?? current;
            if (artist != null)
            {
                await LoadLinksAsync(artist, cancellationToken);
                var conflictingMbid = mbid != null && artist.MusicBrainzArtistId != null && !string.Equals(artist.MusicBrainzArtistId, mbid, StringComparison.OrdinalIgnoreCase);
                var conflictingProvider = provider != null && externalId != null && artist.ProviderLinks.Any(l => l.Provider == provider && l.ExternalId != externalId);
                if (conflictingMbid || conflictingProvider || separateIdentities)
                {
                    await ConflictAsync(track, incoming, source, position, "Incoming identity conflicts with an existing artist credit.", now, cancellationToken);
                    continue;
                }
            }
            if (existing == null && track.ArtistCredits.Any(c => c.Position == position))
            {
                await ConflictAsync(track, incoming, source, position, "Incoming credit disagrees with an occupied credit position.", now, cancellationToken);
                continue;
            }
            if (artist == null)
            {
                artist = new Artist { Id = Guid.NewGuid(), Name = Clean(incoming.CanonicalName) ?? incoming.Name.Trim(), CreatedAt = now, UpdatedAt = now };
                dbContext.Artists.Add(artist);
            }
            artist.MusicBrainzArtistId ??= mbid;
            if (string.IsNullOrWhiteSpace(artist.SortName)) artist.SortName = Clean(incoming.SortName);
            artist.UpdatedAt = now;
            if (provider != null && externalId != null)
            {
                link ??= new ArtistProviderLink { Id = Guid.NewGuid(), ArtistId = artist.Id, Artist = artist, Provider = provider, ExternalId = externalId };
                link.ProfileUrl = Clean(incoming.ProfileUrl) ?? link.ProfileUrl
                    ?? (provider == "musicbrainz" ? $"https://musicbrainz.org/artist/{externalId}" : null);
                link.ArtworkUrl = Clean(incoming.ArtworkUrl) ?? link.ArtworkUrl;
                link.RawMetadata = incoming.RawMetadata ?? link.RawMetadata;
                link.UpdatedAt = now;
                if (dbContext.Entry(link).State == EntityState.Detached) dbContext.ArtistProviderLinks.Add(link);
            }
            if (existing == null)
            {
                existing = new TrackArtistCredit { Id = Guid.NewGuid(), TrackId = track.Id, ArtistId = artist.Id,
                    Artist = artist, Position = position, Role = role, CreditedName = incoming.Name.Trim() };
                track.ArtistCredits.Add(existing);
                dbContext.TrackArtistCredits.Add(existing);
            }
            existing.Artist = artist;
            existing.ArtistId = artist.Id;
            if (incoming.Role != null) existing.Role = role;
            existing.JoinPhrase ??= incoming.JoinPhrase;
            var provenance = existing.Provenance == null ? new Dictionary<string, ArtistCreditMetadata>()
                : JsonSerializer.Deserialize<Dictionary<string, ArtistCreditMetadata>>(existing.Provenance) ?? [];
            provenance[source] = incoming;
            existing.Provenance = JsonSerializer.Serialize(provenance);
        }
    }

    private async Task LoadLinksAsync(Artist artist, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(artist).State != EntityState.Added)
            await dbContext.Entry(artist).Collection(a => a.ProviderLinks).LoadAsync(cancellationToken);
    }

    private async Task ConflictAsync(Track track, ArtistCreditMetadata incoming, string source, int position,
        string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var evidence = JsonSerializer.Serialize(new { source, position, incoming,
            existing = track.ArtistCredits.OrderBy(c => c.Position).Select(c => new { c.ArtistId, c.Position, c.Role, c.CreditedName }) });
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        if (dbContext.ArtistReconciliationConflicts.Local.Any(c => c.TrackId == track.Id && c.EvidenceKey == key)
            || await dbContext.ArtistReconciliationConflicts.AnyAsync(c => c.TrackId == track.Id && c.EvidenceKey == key, cancellationToken)) return;
        dbContext.ArtistReconciliationConflicts.Add(new ArtistReconciliationConflict {
            Id = Guid.NewGuid(), TrackId = track.Id, EvidenceKey = key, Evidence = evidence, Reason = reason, CreatedAt = now });
    }

    private static string Normalize(string? value) => TrackTextNormalizer.Normalize(value);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
