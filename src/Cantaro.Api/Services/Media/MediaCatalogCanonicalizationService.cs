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

public sealed record MediaCatalogCandidate(
    MediaProviderTitleDetails Details,
    bool IsSummary = false,
    string LinkSource = MediaMappingSources.Imported,
    int? LinkedByUserId = null);

public sealed record MediaCatalogResolution(
    IReadOnlyDictionary<MediaCatalogIdentity, MediaProviderLink> Links,
    int CreatedTitles);

/// <summary>Resolves catalog identities for discovery, details, observations, and imports.</summary>
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
                var resolved = await ResolveAsync(db, candidates.Select(ToCatalogCandidate).ToArray(),
                    DateTimeOffset.UtcNow, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return resolved.Links.ToDictionary(pair => pair.Key, pair => pair.Value.MediaTitleId);
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

    internal static bool IsIdentityRace(DbUpdateException exception) =>
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
                nameof(MediaTitle.SortTitle), nameof(MediaTitle.OriginalTitle), nameof(MediaTitle.Synopsis), nameof(MediaTitle.Format),
                nameof(MediaTitle.PosterUrl), nameof(MediaTitle.BackgroundUrl), nameof(MediaTitle.StartYear),
                nameof(MediaTitle.EpisodeCount), nameof(MediaTitle.ChapterCount), nameof(MediaTitle.VolumeCount),
                nameof(MediaTitle.Synonyms)
            }) title.Property(property).IsConcurrencyToken();
        }
    }

    public async Task<MediaCatalogResolution> ResolveAsync(
        ApplicationDbContext db,
        IReadOnlyList<MediaCatalogCandidate> candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = new Dictionary<MediaCatalogIdentity, HashSet<MediaCatalogIdentity>>();
        var validCandidates = candidates.Where(candidate =>
            !string.IsNullOrWhiteSpace(candidate.Details.ProviderId)
            && !string.IsNullOrWhiteSpace(candidate.Details.ProviderMediaId)
            && !string.IsNullOrWhiteSpace(candidate.Details.Title)).ToArray();
        foreach (var candidate in validCandidates)
        {
            var primary = Identity(candidate);
            Connect(graph, primary, primary);
            foreach (var reference in candidate.Details.CrossReferences)
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
        var anchors = matches.Concat(db.MediaProviderLinks.Local).Where(link => graph.ContainsKey(Identity(link)))
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

        var createdTitles = 0;
        var result = new Dictionary<MediaCatalogIdentity, MediaProviderLink>();
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
                .OrderBy(candidate => ProviderPriority(candidate.Details.ProviderId))
                .ThenBy(candidate => Identity(candidate).ProviderId, StringComparer.Ordinal)
                .ThenBy(candidate => Identity(candidate).ProviderMediaId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Details.Title, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0) continue;
            var titles = component.Where(knownLinks.ContainsKey)
                .Select(identity => knownLinks[identity].MediaTitle!).DistinctBy(title => title.Id).ToArray();
            var kinds = entries.Select(entry => entry.Details.MediaKind)
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
                        ApplyMetadata(exact.MediaTitle!, entry, now);
                        RefreshLink(exact, entry, entry.Details.ExternalUrl, now);
                        foreach (var reference in entry.Details.CrossReferences)
                        {
                            var referenceId = MediaCatalogIdentity.Create(reference.ProviderId, reference.ProviderMediaId);
                            if (knownLinks.TryGetValue(referenceId, out var linkedReference)
                                && linkedReference.MediaTitleId == exact.MediaTitleId)
                            {
                                RefreshLink(linkedReference, entry, reference.ExternalUrl, now);
                            }
                        }
                        result[primary] = exact;
                        continue;
                    }
                    var separate = CreateTitle(entry.Details, now);
                    db.MediaTitles.Add(separate);
                    var separateLink = AddLink(db, separate, primary, entry.Details.ExternalUrl, now, entry);
                    ApplyMetadata(separate, entry, now);
                    knownLinks[primary] = separateLink;
                    result[primary] = separateLink;
                    createdTitles++;
                }
                continue;
            }

            var title = titles.SingleOrDefault();
            if (title is null)
            {
                title = CreateTitle(entries[0].Details, now);
                db.MediaTitles.Add(title);
                createdTitles++;
            }
            foreach (var entry in entries) ApplyMetadata(title, entry, now, component.Select(identity => identity.ProviderId));
            foreach (var identity in component)
            {
                var primary = entries.FirstOrDefault(entry => Identity(entry) == identity);
                var referenceSource = entries.FirstOrDefault(entry => !entry.IsSummary && FindReference(entry, identity) is not null)
                    ?? entries.FirstOrDefault(entry => FindReference(entry, identity) is not null);
                var source = primary is { IsSummary: false } ? primary : referenceSource ?? primary;
                var url = source == primary ? primary?.Details.ExternalUrl : FindReference(source!, identity)?.ExternalUrl;
                if (knownLinks.TryGetValue(identity, out var stored))
                {
                    if (source is not null) RefreshLink(stored, source, url, now);
                    continue;
                }
                knownLinks[identity] = AddLink(db, title, identity, url, now, primary ?? referenceSource, primary is null);
            }
            foreach (var entry in entries) result[Identity(entry)] = knownLinks[Identity(entry)];
        }

        // The caller owns SaveChanges so imports and observation resolutions keep
        // catalog and personal-state changes in the same transaction.
        return new MediaCatalogResolution(result, createdTitles);
    }

    private static MediaCatalogIdentity Identity(MediaCatalogCandidate candidate) =>
        MediaCatalogIdentity.Create(candidate.Details.ProviderId, candidate.Details.ProviderMediaId);

    private static MediaCatalogIdentity Identity(MediaProviderLink link) =>
        MediaCatalogIdentity.Create(link.Provider, link.ExternalId);

    private static MediaProviderCrossReference? FindReference(MediaCatalogCandidate candidate, MediaCatalogIdentity identity) =>
        candidate.Details.CrossReferences.FirstOrDefault(reference =>
            MediaCatalogIdentity.Create(reference.ProviderId, reference.ProviderMediaId) == identity);

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

    private static MediaTitle CreateTitle(MediaProviderTitleDetails details, DateTimeOffset now)
    {
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = details.Title,
            MediaKind = details.MediaKind,
            PrimaryProgressDimension = details.PrimaryProgressDimension,
            ReleaseStatusDimension = details.ReleaseStatusDimension,
            CreatedAt = now,
            UpdatedAt = now
        };
        MediaCanonicalMetadataPolicy.Apply(title, details, now);
        return title;
    }

    private static void ApplyMetadata(MediaTitle title, MediaCatalogCandidate candidate, DateTimeOffset now,
        IEnumerable<string>? providers = null)
    {
        if (title.MediaKind != candidate.Details.MediaKind) return;
        if (!candidate.IsSummary && MediaCanonicalMetadataPolicy.ShouldApply(candidate.Details.ProviderId,
                providers ?? title.ProviderLinks.Select(link => link.Provider)))
        {
            MediaCanonicalMetadataPolicy.Apply(title, candidate.Details, now);
        }
        else
        {
            MediaCanonicalMetadataPolicy.FillMissing(title, candidate.Details, now, candidate.IsSummary);
        }
    }

    private static void RefreshLink(MediaProviderLink link, MediaCatalogCandidate candidate, string? url, DateTimeOffset now)
    {
        if (candidate.IsSummary) return;
        link.ExternalUrl = url ?? link.ExternalUrl;
        link.LastVerifiedAt = now;
        link.UpdatedAt = now;
    }

    private static MediaProviderLink AddLink(ApplicationDbContext db, MediaTitle title,
        MediaCatalogIdentity identity, string? url, DateTimeOffset now, MediaCatalogCandidate? source, bool isReference = false)
    {
        var link = new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            MediaTitle = title,
            Provider = identity.ProviderId,
            ExternalId = identity.ProviderMediaId,
            ExternalUrl = url,
            LinkSource = source is { IsSummary: true } ? MediaMappingSources.Automatic
                : isReference ? MediaMappingSources.Imported : source!.LinkSource,
            LinkedByUserId = isReference ? null : source?.LinkedByUserId,
            Confidence = isReference && source is { IsSummary: false } ? 1m : null,
            LastVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MediaProviderLinks.Add(link);
        return link;
    }

    private static MediaCatalogCandidate ToCatalogCandidate(MediaCatalogSearchCandidate candidate)
    {
        var summary = candidate.Result;
        var dimension = summary.MediaKind switch
        {
            MediaKinds.Manga => MediaProgressDimensions.Chapter,
            MediaKinds.Movie => MediaProgressDimensions.CompletionOnly,
            _ => MediaProgressDimensions.Episode
        };
        return new(new MediaProviderTitleDetails
        {
            ProviderId = candidate.ProviderId,
            ProviderMediaId = summary.ProviderMediaId,
            Title = summary.Title,
            NativeTitle = summary.NativeTitle,
            Synonyms = summary.Synonyms,
            MediaKind = summary.MediaKind,
            Format = summary.Format,
            Synopsis = summary.Synopsis,
            PosterUrl = summary.PosterUrl,
            BackgroundUrl = summary.BackgroundUrl,
            ExternalUrl = summary.ExternalUrl,
            StartYear = summary.StartYear,
            EpisodeCount = summary.EpisodeCount,
            ChapterCount = summary.ChapterCount,
            VolumeCount = summary.VolumeCount,
            PrimaryProgressDimension = summary.PrimaryProgressDimension ?? dimension,
            ReleaseStatusDimension = summary.ReleaseStatusDimension ?? dimension,
            CrossReferences = summary.CrossReferences.Select(reference => new MediaProviderCrossReference
            {
                ProviderId = reference.ProviderId,
                ProviderMediaId = reference.ProviderMediaId,
                ExternalUrl = reference.ExternalUrl
            }).ToArray(),
            AvailabilityRefreshSucceeded = false
        }, IsSummary: true, LinkSource: MediaMappingSources.Automatic);
    }
}
