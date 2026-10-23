using Cantaro.Api.Data;

namespace Cantaro.Api.Services;

internal static class CanonicalTrackAccess
{
    // Read access covers owned tracks and their immediate canonical relationships only.
    // Observation metadata must still be scoped separately to the requesting user's entries.
    public static IQueryable<Guid> GetAccessibleTrackIds(ApplicationDbContext dbContext, int userId)
    {
        var owned = dbContext.PlaylistEntries
            .Where(entry => entry.TrackId != null && entry.Playlist != null && entry.Playlist.UserId == userId)
            .Select(entry => entry.TrackId!.Value);
        var sameComposition = dbContext.SongTracks
            .Where(member => dbContext.SongTracks.Any(ownedMember =>
                ownedMember.SongId == member.SongId && owned.Contains(ownedMember.TrackId)))
            .Select(member => member.TrackId);
        var relationTargets = dbContext.TrackRelations
            .Where(relation => owned.Contains(relation.FromTrackId))
            .Select(relation => relation.ToTrackId);
        var relationSources = dbContext.TrackRelations
            .Where(relation => owned.Contains(relation.ToTrackId))
            .Select(relation => relation.FromTrackId);
        return owned.Union(sameComposition).Union(relationTargets).Union(relationSources);
    }
}
