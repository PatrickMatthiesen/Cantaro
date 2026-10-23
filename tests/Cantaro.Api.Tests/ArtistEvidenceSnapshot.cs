using System.Text.Json;
using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Tests;

internal static class ArtistEvidenceSnapshot
{
    public static async Task<string> ReadAsync(ApplicationDbContext db) => JsonSerializer.Serialize(new
    {
        Artists = await db.Artists.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Links = await db.ArtistProviderLinks.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Credits = await db.TrackArtistCredits.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Conflicts = await db.ArtistReconciliationConflicts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Tracks = await db.Tracks.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
}
