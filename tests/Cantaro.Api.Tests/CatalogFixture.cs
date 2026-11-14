using System.Text.Json;
using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;
using Cantaro.Api.Models;
using Cantaro.Api.Services;

namespace Cantaro.Api.Tests;

internal static class CatalogFixture
{
    internal static ProviderCatalogItem Associate(ProviderCatalogItem item, Guid? trackId,
        RecordingAssociationState state = RecordingAssociationState.Verified, long? factsRevision = null)
    {
        item.TrackId = trackId;
        item.AssociationState = state;
        item.AssociationFactsRevision = factsRevision ?? item.Revision;
        item.AssociationRevision = 1;
        item.AssociationMethod = "provider_origin";
        item.AssociationVerifiedAt = item.VerifiedAt;
        var facts = JsonSerializer.Deserialize<ProviderCatalogFacts>(item.FactsJson);
        item.AssociationEvidenceJson = JsonSerializer.Serialize(new
        {
            facts!.Title, facts.Artist, facts.DurationSeconds, facts.Isrc, facts.MusicBrainzRecordingId,
            facts.VersionFlags,
            ArtistCredits = facts.ArtistCredits.Select(credit => new
            {
                credit.Name, credit.CanonicalName, credit.SortName, credit.Provider,
                credit.ExternalId, credit.MusicBrainzArtistId, credit.JoinPhrase, credit.Role
            })
        });
        return item;
    }
    internal static async Task AddNewItemsAsync(ApplicationDbContext db)
    {
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var track in db.ChangeTracker.Entries<Track>().Select(entry => entry.Entity).ToArray())
                foreach (var item in track.ProviderItems)
                    if (db.Entry(item).State == EntityState.Detached
                        && !await db.ProviderCatalogItems.AsNoTracking().AnyAsync(row => row.Id == item.Id))
                        db.ProviderCatalogItems.Add(item);
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = autoDetect; }
    }

    internal static ProviderCatalogFacts? Facts(Track track) => ProviderCatalogPolicy.SharedFacts(track, DateTimeOffset.UtcNow);

    internal static void Verify(ProviderCatalogItem item, ProviderCatalogFacts facts)
    {
        item.FactsJson = JsonSerializer.Serialize(facts);
        item.VisibilityState = CatalogVisibility.Public;
        item.VerificationMethod = item.SourceType switch
        {
            "spotify" => CatalogVerificationMethods.SpotifyApplication,
            "youtube" => CatalogVerificationMethods.YouTubePublicVideo,
            _ => CatalogVerificationMethods.MusicBrainz
        };
        item.VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        item.RevalidateAfter = DateTimeOffset.UtcNow.AddDays(1);
        item.Revision = Math.Max(1, item.Revision);
        Associate(item, item.TrackId);
    }

    internal static string? FactsJson(Track track) => track.ProviderItems.OrderBy(item => item.SourceType, StringComparer.Ordinal)
        .ThenBy(item => item.ExternalId, StringComparer.Ordinal).FirstOrDefault()?.FactsJson;

    internal static void SetFacts(Track track, string json, string sourceType = "spotify")
    {
        var item = ProviderCatalogPolicy.SharedSource(track, DateTimeOffset.UtcNow);
        if (item is null)
        {
            item = new ProviderCatalogItem { Id = Guid.NewGuid(), SourceType = sourceType, ExternalId = track.Id.ToString(), FactsJson = json };
            track.ProviderItems.Add(item);
        }
        Verify(item, JsonSerializer.Deserialize<ProviderCatalogFacts>(json)!);
        item.TrackId = track.Id;
        item.Revision++;
        item.AssociationFactsRevision = item.Revision;
    }

    internal static void SetTitle(Track track, string? title, string sourceType = "spotify")
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(Facts(track) ?? new ProviderCatalogFacts { Title = "Song" })!.AsObject();
        node["Title"] = title;
        SetFacts(track, node.ToJsonString(), sourceType);
    }

    internal static void SetArtist(Track track, string? artist)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(Facts(track) ?? new ProviderCatalogFacts { Title = "Song" })!.AsObject();
        node["Artist"] = artist;
        SetFacts(track, node.ToJsonString());
    }

    internal static bool SetArtwork(Track track, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var node = System.Text.Json.JsonSerializer.SerializeToNode(Facts(track) ?? new ProviderCatalogFacts { Title = "Song" })!.AsObject();
        if (node["ThumbnailUrl"] is not null) return false;
        node["ThumbnailUrl"] = url;
        SetFacts(track, node.ToJsonString());
        return true;
    }

}
