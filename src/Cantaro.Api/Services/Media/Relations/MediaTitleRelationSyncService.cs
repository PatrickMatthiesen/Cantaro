using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MediaTitleRelationSyncService(
    ApplicationDbContext dbContext,
    ILogger<MediaTitleRelationSyncService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaTitleRelationSyncService> _logger = logger;

    public async Task<MediaTitleRelationSyncResult> SyncAsync(
        int userId,
        IMediaRelationGraphProvider provider,
        string providerMediaId,
        CancellationToken cancellationToken)
    {
        var snapshot = await provider.GetRelationGraphAsync(
            userId,
            providerMediaId,
            cancellationToken);
        if (!string.Equals(snapshot.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Relation graph provider '{provider.ProviderId}' returned a '{snapshot.ProviderId}' snapshot.");
        }

        return await PersistAsync(snapshot, cancellationToken);
    }

    internal async Task<MediaTitleRelationSyncResult> PersistAsync(
        MediaProviderRelationGraphSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var relationSnapshotId = Guid.NewGuid();
        var nodesByProviderId = snapshot.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.ProviderMediaId))
            .GroupBy(node => node.ProviderMediaId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var providerMediaIds = nodesByProviderId.Keys.ToList();
        var existingLinks = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
            .Where(link => link.Provider == snapshot.ProviderId
                && providerMediaIds.Contains(link.ExternalId))
            .ToDictionaryAsync(link => link.ExternalId, StringComparer.Ordinal, cancellationToken);
        var titlesByProviderId = new Dictionary<string, MediaTitle>(StringComparer.Ordinal);
        var createdTitles = 0;

        foreach (var (externalId, node) in nodesByProviderId)
        {
            if (!existingLinks.TryGetValue(externalId, out var link))
            {
                var title = CreateTitle(node, now);
                link = new MediaProviderLink
                {
                    Id = Guid.NewGuid(),
                    MediaTitleId = title.Id,
                    Provider = snapshot.ProviderId,
                    ExternalId = externalId,
                    ExternalUrl = node.ExternalUrl,
                    LinkSource = MediaMappingSources.Automatic,
                    LastVerifiedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                    MediaTitle = title
                };
                _dbContext.MediaTitles.Add(title);
                _dbContext.MediaProviderLinks.Add(link);
                existingLinks[externalId] = link;
                createdTitles++;
            }
            else if (link.MediaTitle is { } existingTitle)
            {
                ApplyNode(existingTitle, node, now);
                link.ExternalUrl = node.ExternalUrl ?? link.ExternalUrl;
                link.LastVerifiedAt = now;
                link.UpdatedAt = now;
            }

            if (link.MediaTitle is not null)
            {
                titlesByProviderId[externalId] = link.MediaTitle;
            }
        }

        var sourceTitleIds = snapshot.RefreshedProviderMediaIds
            .Where(titlesByProviderId.ContainsKey)
            .Select(id => titlesByProviderId[id].Id)
            .Distinct()
            .ToList();
        foreach (var providerMediaId in snapshot.RefreshedProviderMediaIds)
        {
            if (existingLinks.TryGetValue(providerMediaId, out var refreshedLink))
            {
                refreshedLink.RelationsLastVerifiedAt = now;
                refreshedLink.RelationsSnapshotId = relationSnapshotId;
            }
        }

        var existingRelations = await _dbContext.MediaTitleRelations
            .Where(relation => relation.SourceProvider == snapshot.ProviderId
                && sourceTitleIds.Contains(relation.MediaTitleId))
            .ToListAsync(cancellationToken);
        var existingRelationsByKey = existingRelations.ToDictionary(RelationKey, StringComparer.Ordinal);
        var desiredKeys = new HashSet<string>(StringComparer.Ordinal);
        var createdRelations = 0;

        foreach (var edge in snapshot.Edges)
        {
            if (!titlesByProviderId.TryGetValue(edge.MediaProviderMediaId, out var sourceTitle)
                || !titlesByProviderId.TryGetValue(edge.RelatedProviderMediaId, out var relatedTitle)
                || sourceTitle.Id == relatedTitle.Id
                || string.IsNullOrWhiteSpace(edge.RelationType))
            {
                continue;
            }

            var relationType = edge.RelationType.Trim().ToLowerInvariant();
            var key = RelationKey(
                sourceTitle.Id,
                relatedTitle.Id,
                relationType,
                snapshot.ProviderId);
            desiredKeys.Add(key);
            if (existingRelationsByKey.TryGetValue(key, out var existingRelation))
            {
                existingRelation.SourceRelationId = edge.SourceRelationId ?? existingRelation.SourceRelationId;
                existingRelation.LastVerifiedAt = now;
                continue;
            }

            _dbContext.MediaTitleRelations.Add(new MediaTitleRelation
            {
                Id = Guid.NewGuid(),
                MediaTitleId = sourceTitle.Id,
                RelatedMediaTitleId = relatedTitle.Id,
                RelationType = relationType,
                SourceProvider = snapshot.ProviderId,
                SourceRelationId = edge.SourceRelationId,
                FirstSeenAt = now,
                LastVerifiedAt = now
            });
            createdRelations++;
        }

        // RefreshedProviderMediaIds means each listed source's complete outgoing
        // collection was read. A traversal can be globally truncated while
        // those individual source snapshots are still safe to prune.
        var staleRelations = existingRelations
            .Where(relation => !desiredKeys.Contains(RelationKey(relation)))
            .ToList();
        _dbContext.MediaTitleRelations.RemoveRange(staleRelations);
        var removedRelations = staleRelations.Count;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Synchronized {Provider} relation graph rooted at {ProviderMediaId}: {NodeCount} nodes, {EdgeCount} edges, complete={IsComplete}.",
            snapshot.ProviderId,
            snapshot.RootProviderMediaId,
            nodesByProviderId.Count,
            desiredKeys.Count,
            snapshot.IsComplete);

        return new MediaTitleRelationSyncResult
        {
            RootMediaTitleId = titlesByProviderId.TryGetValue(snapshot.RootProviderMediaId, out var root)
                ? root.Id
                : null,
            CreatedTitles = createdTitles,
            CreatedRelations = createdRelations,
            RemovedRelations = removedRelations,
            IsComplete = snapshot.IsComplete
        };
    }

    private static MediaTitle CreateTitle(MediaProviderRelationGraphNode node, DateTimeOffset now)
    {
        var dimensions = GetDimensions(node.MediaKind);
        return new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = node.Title,
            SortTitle = node.Title,
            OriginalTitle = node.NativeTitle,
            Synonyms = [.. node.Synonyms],
            MediaKind = node.MediaKind,
            Format = node.Format,
            Synopsis = node.Synopsis,
            PosterUrl = node.PosterUrl,
            BackgroundUrl = node.BackgroundUrl,
            StartYear = node.StartYear,
            EpisodeCount = node.EpisodeCount,
            ChapterCount = node.ChapterCount,
            VolumeCount = node.VolumeCount,
            SupportsEpisodeProgress = dimensions.Primary == MediaProgressDimensions.Episode,
            SupportsChapterProgress = dimensions.Primary == MediaProgressDimensions.Chapter,
            SupportsVolumeProgress = dimensions.Primary == MediaProgressDimensions.Volume,
            IsCompletionOnly = dimensions.Primary == MediaProgressDimensions.CompletionOnly,
            PrimaryProgressDimension = dimensions.Primary,
            ReleaseStatusDimension = dimensions.Release,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static void ApplyNode(
        MediaTitle title,
        MediaProviderRelationGraphNode node,
        DateTimeOffset now)
    {
        var dimensions = GetDimensions(node.MediaKind);
        title.CanonicalTitle = node.Title;
        title.SortTitle = node.Title;
        title.OriginalTitle = node.NativeTitle ?? title.OriginalTitle;
        title.Synonyms = [.. node.Synonyms];
        title.MediaKind = node.MediaKind;
        title.Format = node.Format ?? title.Format;
        title.Synopsis = node.Synopsis ?? title.Synopsis;
        title.PosterUrl = node.PosterUrl ?? title.PosterUrl;
        title.BackgroundUrl = node.BackgroundUrl ?? title.BackgroundUrl;
        title.StartYear = node.StartYear ?? title.StartYear;
        title.EpisodeCount = node.EpisodeCount ?? title.EpisodeCount;
        title.ChapterCount = node.ChapterCount ?? title.ChapterCount;
        title.VolumeCount = node.VolumeCount ?? title.VolumeCount;
        title.SupportsEpisodeProgress = dimensions.Primary == MediaProgressDimensions.Episode;
        title.SupportsChapterProgress = dimensions.Primary == MediaProgressDimensions.Chapter;
        title.SupportsVolumeProgress = dimensions.Primary == MediaProgressDimensions.Volume;
        title.IsCompletionOnly = dimensions.Primary == MediaProgressDimensions.CompletionOnly;
        title.PrimaryProgressDimension = dimensions.Primary;
        title.ReleaseStatusDimension = dimensions.Release;
        title.UpdatedAt = now;
    }

    private static (string Primary, string Release) GetDimensions(string mediaKind)
    {
        return mediaKind switch
        {
            MediaKinds.Anime => (MediaProgressDimensions.Episode, MediaProgressDimensions.Episode),
            MediaKinds.Manga => (MediaProgressDimensions.Chapter, MediaProgressDimensions.Chapter),
            _ => (MediaProgressDimensions.Unavailable, MediaProgressDimensions.Unavailable)
        };
    }

    private static string RelationKey(MediaTitleRelation relation)
        => RelationKey(
            relation.MediaTitleId,
            relation.RelatedMediaTitleId,
            relation.RelationType,
            relation.SourceProvider);

    private static string RelationKey(
        Guid mediaTitleId,
        Guid relatedMediaTitleId,
        string relationType,
        string sourceProvider)
        => $"{mediaTitleId:N}:{relatedMediaTitleId:N}:{relationType}:{sourceProvider}";
}
