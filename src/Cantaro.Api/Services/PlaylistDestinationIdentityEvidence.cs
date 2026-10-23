using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

// Shared eligibility for review and resolution. Global canonical mappings alone
// never authorize a destination ID for the requester.
internal static class PlaylistDestinationIdentityEvidence
{
    public static async Task<string?> FindAsync(ApplicationDbContext db, PlatformAccountContext account,
        string service, PlaylistEntry entry, CancellationToken ct, ServicePlaylistMapping? link = null)
    {
        var observation = entry.TrackObservation;
        var trackId = entry.TrackId ?? observation?.TrackId;
        if (observation?.SourceType == service && (trackId is null || observation.TrackId == trackId))
            return observation.ExternalId;
        if (trackId is not null)
        {
            var owned = await db.PlaylistEntries.AsNoTracking()
                .Where(item => item.Playlist!.UserId == account.UserId && item.TrackId == trackId
                    && item.TrackObservation != null && item.TrackObservation.SourceType == service
                    && item.TrackObservation.TrackId == trackId)
                .OrderBy(item => item.TrackObservation!.ExternalId)
                .Select(item => item.TrackObservation!.ExternalId).FirstOrDefaultAsync(ct);
            if (owned is not null) return owned;
        }

        var links = link is null
            ? await db.ServicePlaylistMappings.AsNoTracking().Include(item => item.ConnectedServiceAccount)
                .Where(item => item.PlaylistId == entry.PlaylistId && item.UserId == account.UserId
                    && item.Playlist!.UserId == account.UserId && item.Service == service
                    && item.ConnectedServiceAccountId == account.ConnectedServiceAccountId && item.State == "active")
                .ToListAsync(ct)
            : [link];
        foreach (var candidate in links)
        {
            var connected = candidate.ConnectedServiceAccount;
            if (candidate.UserId != account.UserId || candidate.PlaylistId != entry.PlaylistId
                || candidate.Service != service || candidate.State != "active"
                || candidate.ConnectedServiceAccountId != account.ConnectedServiceAccountId
                || connected is null || connected.UserId != account.UserId || connected.Service != service
                || connected.ConnectionState != "connected" || connected.ExternalAccountId != candidate.ExternalAccountId
                || account.ExpectedExternalAccountId is { } expected && expected != candidate.ExternalAccountId)
                continue;
            var progress = PlaylistMatchingProgress.Read(candidate.MatchingProgressJson);
            if (!progress.TryGetValue(entry.Id, out var saved) || string.IsNullOrWhiteSpace(saved.ExternalId)
                || !PlaylistMatchingProgress.CanReuse(saved, entry, candidate)) continue;
            // Preserve the canonical identity conflict guard if a provider key was reassigned.
            if (await db.TrackSourceIds.AsNoTracking().AnyAsync(source => source.SourceType == service
                && source.ExternalId == saved.ExternalId && source.TrackId != trackId, ct)) continue;
            return saved.ExternalId;
        }
        return null;
    }
}
