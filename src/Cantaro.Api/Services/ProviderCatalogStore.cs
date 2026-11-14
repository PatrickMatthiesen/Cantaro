using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

/// <summary>Persists provider facts independently of the caller's playlist transaction.</summary>
public sealed class ProviderCatalogStore(IServiceScopeFactory scopes, TimeProvider clock)
{
    // Public availability changes are detected on the next provider verification.
    // These leases bound exposure when a later verification cannot reach the provider.
    private static readonly IReadOnlyDictionary<string, TimeSpan> Freshness = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
    {
        ["spotify"] = TimeSpan.FromDays(30),
        ["youtube"] = TimeSpan.FromDays(7),
        ["musicbrainz"] = TimeSpan.FromDays(90)
    };

    internal async Task<ProviderCatalogItem> RecordVerifiedAsync(
        string sourceType, string externalId, string verificationMethod,
        ProviderCatalogFacts facts, CancellationToken ct)
    {
        if (!CatalogVerificationMethods.Matches(sourceType, verificationMethod))
            throw new ArgumentException("The verification method does not match the provider.", nameof(verificationMethod));
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(facts.Title))
            throw new ArgumentException("A provider item needs an ID and title.");

        var sanitizedFacts = Sanitize(facts);
        var json = JsonSerializer.Serialize(sanitizedFacts);
        var normalized = ProviderCatalogNormalizedFacts.FromFacts(sanitizedFacts);
        var now = clock.GetUtcNow();
        var expires = now + Freshness[sourceType];

        // The dedicated scope avoids saving any caller-owned playlist changes.
        // PostgreSQL and SQLite both support this conflict-safe upsert syntax.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sqlite = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
            object verifiedAt = sqlite ? now.UtcTicks : now;
            object revalidateAfter = sqlite ? expires.UtcTicks : expires;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProviderCatalogItems"
                    ("Id", "SourceType", "ExternalId", "FactsJson", "NormalizedTitle", "NormalizedArtist", "NormalizedIsrc", "NormalizedMusicBrainzRecordingId", "VerificationMethod", "VerifiedAt", "RevalidateAfter", "VisibilityState", "Revision", "AssociationState", "AssociationRevision", "PresentationKind")
                VALUES
                    ({Guid.NewGuid()}, {sourceType}, {externalId}, {json}, {normalized.Title}, {normalized.Artist}, {normalized.Isrc}, {normalized.MusicBrainzRecordingId}, {verificationMethod}, {verifiedAt}, {revalidateAfter}, {(int)CatalogVisibility.Public}, {1L}, {(int)RecordingAssociationState.Unknown}, {0L}, {(int)TrackSourcePresentationKind.Unknown})
                ON CONFLICT ("SourceType", "ExternalId") DO UPDATE SET
                    "Revision" = CASE
                        WHEN "ProviderCatalogItems"."FactsJson" <> EXCLUDED."FactsJson"
                          OR "ProviderCatalogItems"."VisibilityState" <> EXCLUDED."VisibilityState"
                        THEN "ProviderCatalogItems"."Revision" + 1
                        ELSE "ProviderCatalogItems"."Revision"
                    END,
                    "FactsJson" = EXCLUDED."FactsJson",
                    "NormalizedTitle" = EXCLUDED."NormalizedTitle",
                    "NormalizedArtist" = EXCLUDED."NormalizedArtist",
                    "NormalizedIsrc" = EXCLUDED."NormalizedIsrc",
                    "NormalizedMusicBrainzRecordingId" = EXCLUDED."NormalizedMusicBrainzRecordingId",
                    "VerificationMethod" = EXCLUDED."VerificationMethod",
                    "VerifiedAt" = EXCLUDED."VerifiedAt",
                    "RevalidateAfter" = EXCLUDED."RevalidateAfter",
                    "VisibilityState" = EXCLUDED."VisibilityState"
                """, ct);
        }

        await using var readScope = scopes.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await readDb.ProviderCatalogItems.AsNoTracking()
            .SingleAsync(item => item.SourceType == sourceType && item.ExternalId == externalId, ct);
    }

    internal async Task<ProviderCatalogItem?> FindAsync(string sourceType, string externalId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ProviderCatalogItems.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SourceType == sourceType && item.ExternalId == externalId, ct);
    }

    internal async Task<IReadOnlyList<ProviderCatalogItem>> FindManyAsync(
        string sourceType, IReadOnlyCollection<string> externalIds, CancellationToken ct)
    {
        if (externalIds.Count == 0) return [];
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ProviderCatalogItems.AsNoTracking()
            .Where(item => item.SourceType == sourceType && externalIds.Contains(item.ExternalId))
            .ToListAsync(ct);
    }

    internal async Task MarkWithdrawnAsync(string sourceType, string externalId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        object revalidateAfter = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? now.UtcTicks : now;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ProviderCatalogItems"
            SET "VisibilityState" = {(int)CatalogVisibility.Withdrawn},
                "RevalidateAfter" = {revalidateAfter},
                "Revision" = "Revision" + CASE WHEN "VisibilityState" <> {(int)CatalogVisibility.Withdrawn} THEN 1 ELSE 0 END
            WHERE "SourceType" = {sourceType} AND "ExternalId" = {externalId}
            """, ct);
    }

    private static ProviderCatalogFacts Sanitize(ProviderCatalogFacts facts) => new()
    {
        Title = facts.Title.Trim(), Artist = Clean(facts.Artist),
        Albums = facts.Albums.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray(),
        ThumbnailUrl = CleanUrl(facts.ThumbnailUrl), ProviderUrl = CleanUrl(facts.ProviderUrl),
        DurationSeconds = facts.DurationSeconds is >= 0 ? facts.DurationSeconds : null,
        Isrc = Clean(facts.Isrc), MusicBrainzRecordingId = Clean(facts.MusicBrainzRecordingId),
        ArtistCredits = facts.ArtistCredits.Where(credit => !string.IsNullOrWhiteSpace(credit.Name))
            .Select(credit => credit with { RawMetadata = null }).ToArray(),
        VersionFlags = facts.VersionFlags,
        OriginalTitle = Clean(facts.OriginalTitle), ChannelTitle = Clean(facts.ChannelTitle),
        ChannelId = Clean(facts.ChannelId), Description = Clean(facts.Description),
        CategoryId = Clean(facts.CategoryId),
        TopicCategories = facts.TopicCategories.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray()
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? CleanUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
}
