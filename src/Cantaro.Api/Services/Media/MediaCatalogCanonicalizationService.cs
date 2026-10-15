using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cantaro.Api.Services;

public readonly record struct MediaCatalogIdentity(string ProviderId, string ProviderMediaId)
{
    public static MediaCatalogIdentity Create(string providerId, string providerMediaId) =>
        new(providerId.Trim().ToLowerInvariant(), providerMediaId.Trim());
}

public sealed record MediaCatalogSearchCandidate(string ProviderId, CachedMediaProviderSearchResult Result);

/// <summary>Persists public discovery summaries without importing anyone's library.</summary>
public sealed class MediaCatalogCanonicalizationService(
    DbContextOptions<ApplicationDbContext> options,
    ILogger<MediaCatalogCanonicalizationService> logger)
{
    public async Task<IReadOnlyDictionary<MediaCatalogIdentity, Guid>> CanonicalizeAsync(
        IReadOnlyList<MediaCatalogSearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return new Dictionary<MediaCatalogIdentity, Guid>();

        // Each retry starts with fresh tracking state, including when an import won
        // the same identity concurrently. A failed atomic save leaves no orphan title.
        for (var attempt = 0; ; attempt++)
        {
            await using var db = new CatalogWriteContext(options);
            try
            {
                return await PersistAsync(db, candidates, cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt < 2 && IsIdentityRace(exception))
            {
                logger.LogDebug(exception, "Retrying media catalog identity race, attempt {Attempt}.", attempt + 1);
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < 2)
            {
                logger.LogDebug(exception, "Retrying concurrent media catalog metadata update, attempt {Attempt}.", attempt + 1);
            }
        }
    }

    private static bool IsIdentityRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_MediaProviderLinks_Provider_ExternalId" or "IX_MediaProviderLinks_MediaTitleId_Provider"
        };

    // Search summaries may fill missing fields, but detail/import writers can
    // populate them after our preload. Compare original column values on UPDATE
    // and recompute on conflict. This model is private to discovery writes and
    // requires no database column or changes to other writers' EF models.
    private sealed class CatalogWriteContext(DbContextOptions<ApplicationDbContext> contextOptions)
        : ApplicationDbContext(contextOptions)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var title = builder.Entity<MediaTitle>();
            foreach (var property in new[]
            {
                nameof(MediaTitle.OriginalTitle), nameof(MediaTitle.Synopsis), nameof(MediaTitle.Format),
                nameof(MediaTitle.PosterUrl), nameof(MediaTitle.BackgroundUrl), nameof(MediaTitle.StartYear),
                nameof(MediaTitle.EpisodeCount), nameof(MediaTitle.ChapterCount), nameof(MediaTitle.VolumeCount),
                nameof(MediaTitle.Synonyms)
            }) title.Property(property).IsConcurrencyToken();
        }
    }

    private static async Task<IReadOnlyDictionary<MediaCatalogIdentity, Guid>> PersistAsync(
        ApplicationDbContext db,
        IReadOnlyList<MediaCatalogSearchCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = new Dictionary<MediaCatalogIdentity, HashSet<MediaCatalogIdentity>>();
        var validCandidates = candidates.Where(candidate =>
            !string.IsNullOrWhiteSpace(candidate.ProviderId)
            && !string.IsNullOrWhiteSpace(candidate.Result.ProviderMediaId)
            && !string.IsNullOrWhiteSpace(candidate.Result.Title)).ToArray();
        foreach (var candidate in validCandidates)
        {
            var primary = Identity(candidate);
            Connect(graph, primary, primary);
            foreach (var reference in candidate.Result.CrossReferences)
            {
                if (string.IsNullOrWhiteSpace(reference.ProviderId)
                    || string.IsNullOrWhiteSpace(reference.ProviderMediaId)) continue;
                Connect(graph, primary, MediaCatalogIdentity.Create(reference.ProviderId, reference.ProviderMediaId));
            }
        }

        var providers = graph.Keys.Select(key => key.ProviderId).Distinct().ToArray();
        var externalIds = graph.Keys.Select(key => key.ProviderMediaId).Distinct().ToArray();
        var matches = await db.MediaProviderLinks
            .Include(link => link.MediaTitle)!.ThenInclude(title => title!.ProviderLinks)
            .Where(link => providers.Contains(link.Provider) && externalIds.Contains(link.ExternalId))
            .ToListAsync(cancellationToken);
        var anchors = matches.Where(link => graph.ContainsKey(Identity(link)))
            .Select(link => link.MediaTitle!).DistinctBy(title => title.Id).ToArray();
        var knownLinks = anchors.SelectMany(title => title.ProviderLinks)
            .ToDictionary(Identity);

        // Existing aliases connect candidates too. Otherwise two separate input
        // components could each attach different IDs for one provider to one title.
        foreach (var title in anchors)
        {
            var identities = title.ProviderLinks.Select(Identity).ToArray();
            foreach (var identity in identities) Connect(graph, identities[0], identity);
        }

        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<MediaCatalogIdentity, Guid>();
        var remaining = graph.Keys.ToHashSet();
        foreach (var seed in graph.Keys)
        {
            if (!remaining.Remove(seed)) continue;
            var component = new HashSet<MediaCatalogIdentity> { seed };
            var queue = new Queue<MediaCatalogIdentity>();
            queue.Enqueue(seed);
            while (queue.TryDequeue(out var current))
            {
                foreach (var neighbor in graph[current])
                {
                    if (!remaining.Remove(neighbor)) continue;
                    component.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            var entries = validCandidates.Where(candidate => component.Contains(Identity(candidate)))
                .OrderBy(candidate => ProviderPriority(candidate.ProviderId))
                .ThenBy(candidate => Identity(candidate).ProviderId, StringComparer.Ordinal)
                .ThenBy(candidate => Identity(candidate).ProviderMediaId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Result.Title, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0) continue;
            var titles = component.Where(knownLinks.ContainsKey)
                .Select(identity => knownLinks[identity].MediaTitle!).DistinctBy(title => title.Id).ToArray();
            var kinds = entries.Select(entry => entry.Result.MediaKind)
                .Concat(titles.Select(title => title.MediaKind)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var compatible = titles.Length <= 1 && kinds.Length == 1
                && component.GroupBy(identity => identity.ProviderId).All(group => group.Count() == 1)
                && component.All(identity => IsKindCompatible(identity, kinds[0]));
            if (!compatible)
            {
                // Never give an ambiguous shared reference to whichever entry
                // happened to be encountered first.
                foreach (var entry in entries.DistinctBy(Identity))
                {
                    var primary = Identity(entry);
                    if (knownLinks.TryGetValue(primary, out var exact))
                    {
                        result[primary] = exact.MediaTitleId;
                        continue;
                    }
                    var separate = CreateTitle(entry.Result, now);
                    db.MediaTitles.Add(separate);
                    AddLink(db, separate, primary, entry.Result.ExternalUrl, now);
                    result[primary] = separate.Id;
                }
                continue;
            }

            var title = titles.SingleOrDefault();
            if (title is null)
            {
                title = CreateTitle(entries[0].Result, now);
                db.MediaTitles.Add(title);
            }
            foreach (var entry in entries) FillMissingMetadata(title, entry.Result);
            foreach (var identity in component)
            {
                if (knownLinks.ContainsKey(identity)) continue;
                var primary = entries.FirstOrDefault(entry => Identity(entry) == identity);
                var url = primary?.Result.ExternalUrl ?? entries.SelectMany(entry => entry.Result.CrossReferences)
                    .FirstOrDefault(reference => MediaCatalogIdentity.Create(reference.ProviderId, reference.ProviderMediaId) == identity)?.ExternalUrl;
                AddLink(db, title, identity, url, now);
            }
            foreach (var entry in entries) result[Identity(entry)] = title.Id;
        }

        // EF's relational SaveChanges is atomic across its INSERT/UPDATE commands.
        // There are no provider requests or user-state writes in this service.
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static MediaCatalogIdentity Identity(MediaCatalogSearchCandidate candidate) =>
        MediaCatalogIdentity.Create(candidate.ProviderId, candidate.Result.ProviderMediaId);

    private static MediaCatalogIdentity Identity(MediaProviderLink link) =>
        MediaCatalogIdentity.Create(link.Provider, link.ExternalId);

    private static void Connect(
        Dictionary<MediaCatalogIdentity, HashSet<MediaCatalogIdentity>> graph,
        MediaCatalogIdentity left,
        MediaCatalogIdentity right)
    {
        if (!graph.TryGetValue(left, out var leftEdges)) graph[left] = leftEdges = [];
        if (!graph.TryGetValue(right, out var rightEdges)) graph[right] = rightEdges = [];
        leftEdges.Add(right);
        rightEdges.Add(left);
    }

    private static bool IsKindCompatible(MediaCatalogIdentity identity, string kind)
    {
        if (identity.ProviderId is "myanimelist" or "simkl")
        {
            var separator = identity.ProviderMediaId.IndexOf(':');
            if (separator < 0) return false;
            var expectedKind = identity.ProviderMediaId[..separator] switch
            {
                "anime" => MediaKinds.Anime,
                "manga" => MediaKinds.Manga,
                "tv" => MediaKinds.Series,
                "movie" => MediaKinds.Movie,
                _ => null
            };
            return expectedKind == kind;
        }
        return true;
    }

    private static int ProviderPriority(string providerId) => providerId.Trim().ToLowerInvariant() switch
    {
        "anilist" => 0,
        "myanimelist" => 1,
        "simkl" => 2,
        _ => 3
    };

    private static MediaTitle CreateTitle(CachedMediaProviderSearchResult summary, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalTitle = summary.Title,
        SortTitle = summary.Title,
        MediaKind = summary.MediaKind,
        PrimaryProgressDimension = summary.PrimaryProgressDimension ?? DefaultDimension(summary.MediaKind),
        ReleaseStatusDimension = summary.ReleaseStatusDimension ?? DefaultDimension(summary.MediaKind),
        SupportsEpisodeProgress = (summary.PrimaryProgressDimension ?? DefaultDimension(summary.MediaKind)) == MediaProgressDimensions.Episode,
        SupportsChapterProgress = (summary.PrimaryProgressDimension ?? DefaultDimension(summary.MediaKind)) == MediaProgressDimensions.Chapter,
        SupportsVolumeProgress = (summary.PrimaryProgressDimension ?? DefaultDimension(summary.MediaKind)) == MediaProgressDimensions.Volume,
        IsCompletionOnly = (summary.PrimaryProgressDimension ?? DefaultDimension(summary.MediaKind)) == MediaProgressDimensions.CompletionOnly,
        CreatedAt = now,
        UpdatedAt = now,
        OriginalTitle = summary.NativeTitle,
        Synopsis = summary.Synopsis,
        Format = summary.Format,
        PosterUrl = summary.PosterUrl,
        BackgroundUrl = summary.BackgroundUrl,
        StartYear = summary.StartYear,
        EpisodeCount = summary.EpisodeCount,
        ChapterCount = summary.ChapterCount,
        VolumeCount = summary.VolumeCount,
        Synonyms = summary.Synonyms.Append(summary.Title).Append(summary.NativeTitle ?? "").Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList()
    };

    private static string DefaultDimension(string kind) => kind switch
    {
        MediaKinds.Manga => MediaProgressDimensions.Chapter,
        MediaKinds.Movie => MediaProgressDimensions.CompletionOnly,
        _ => MediaProgressDimensions.Episode
    };

    private static void FillMissingMetadata(MediaTitle title, CachedMediaProviderSearchResult summary)
    {
        title.OriginalTitle ??= summary.NativeTitle;
        title.Synopsis ??= summary.Synopsis;
        title.Format ??= summary.Format;
        title.PosterUrl ??= summary.PosterUrl;
        title.BackgroundUrl ??= summary.BackgroundUrl;
        title.StartYear ??= summary.StartYear;
        title.EpisodeCount ??= summary.EpisodeCount;
        title.ChapterCount ??= summary.ChapterCount;
        title.VolumeCount ??= summary.VolumeCount;
        var synonyms = title.Synonyms.Concat(summary.Synonyms).Append(summary.Title).Append(summary.NativeTitle ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (!title.Synonyms.SequenceEqual(synonyms)) title.Synonyms = [.. synonyms];
    }

    private static void AddLink(ApplicationDbContext db, MediaTitle title,
        MediaCatalogIdentity identity, string? url, DateTimeOffset now) => db.MediaProviderLinks.Add(new MediaProviderLink
    {
        Id = Guid.NewGuid(),
        MediaTitleId = title.Id,
        MediaTitle = title,
        Provider = identity.ProviderId,
        ExternalId = identity.ProviderMediaId,
        ExternalUrl = url,
        LinkSource = MediaMappingSources.Automatic,
        LastVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    });
}
