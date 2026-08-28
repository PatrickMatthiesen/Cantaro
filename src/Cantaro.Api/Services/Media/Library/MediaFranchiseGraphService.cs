using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MediaFranchiseGraphService(ApplicationDbContext dbContext)
{
    private const string AniListProvider = "anilist";
    private const int MaxNodes = 50;
    private const int MaxTraversalDepth = 8;
    private static readonly string[] FranchiseTraversalRelationTypes =
    [
        MediaRelationTypes.Adaptation,
        MediaRelationTypes.Alternative,
        MediaRelationTypes.Compilation,
        MediaRelationTypes.Contains,
        MediaRelationTypes.Parent,
        MediaRelationTypes.Prequel,
        MediaRelationTypes.Sequel,
        MediaRelationTypes.SideStory,
        MediaRelationTypes.Source,
        MediaRelationTypes.SpinOff,
        MediaRelationTypes.Summary
    ];
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<MediaFranchiseGraphDto?> GetAsync(
        int? userId,
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var rootExists = await _dbContext.MediaTitles
            .AsNoTracking()
            .AnyAsync(title => title.Id == mediaTitleId, cancellationToken);
        if (!rootExists)
        {
            return null;
        }

        var rootSnapshotId = await _dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => link.MediaTitleId == mediaTitleId && link.Provider == AniListProvider)
            .Select(link => link.RelationsSnapshotId)
            .SingleOrDefaultAsync(cancellationToken);
        var titleIds = new HashSet<Guid> { mediaTitleId };
        var frontier = new HashSet<Guid> { mediaTitleId };
        var relationsById = new Dictionary<Guid, MediaTitleRelation>();

        var hasStructuralRootRelation = await _dbContext.MediaTitleRelations
            .AsNoTracking()
            .AnyAsync(relation => relation.SourceProvider == AniListProvider
                && FranchiseTraversalRelationTypes.Contains(relation.RelationType)
                && (relation.MediaTitleId == mediaTitleId
                    || relation.RelatedMediaTitleId == mediaTitleId), cancellationToken);
        if (!hasStructuralRootRelation && rootSnapshotId is not null)
        {
            var directNeighborIds = await _dbContext.MediaTitleRelations
                .AsNoTracking()
                .Where(relation => relation.SourceProvider == AniListProvider
                    && (relation.MediaTitleId == mediaTitleId
                        || relation.RelatedMediaTitleId == mediaTitleId))
                .Select(relation => relation.MediaTitleId == mediaTitleId
                    ? relation.RelatedMediaTitleId
                    : relation.MediaTitleId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var snapshotAnchorIds = await _dbContext.MediaProviderLinks
                .AsNoTracking()
                .Where(link => directNeighborIds.Contains(link.MediaTitleId)
                    && link.Provider == AniListProvider
                    && link.RelationsSnapshotId == rootSnapshotId)
                .Select(link => link.MediaTitleId)
                .ToArrayAsync(cancellationToken);
            foreach (var anchorId in snapshotAnchorIds)
            {
                AddIfWithinBound(anchorId, titleIds, frontier);
            }
        }

        for (var depth = 0; depth < MaxTraversalDepth && frontier.Count > 0 && titleIds.Count < MaxNodes; depth++)
        {
            var frontierIds = frontier.ToArray();
            var relations = await _dbContext.MediaTitleRelations
                .AsNoTracking()
                .Include(relation => relation.MediaTitle)
                .Include(relation => relation.RelatedMediaTitle)
                .Where(relation => relation.SourceProvider == AniListProvider
                    && FranchiseTraversalRelationTypes.Contains(relation.RelationType)
                    && (frontierIds.Contains(relation.MediaTitleId)
                        || frontierIds.Contains(relation.RelatedMediaTitleId)))
                .ToListAsync(cancellationToken);

            var nextFrontier = new HashSet<Guid>();
            foreach (var relation in relations)
            {
                relationsById[relation.Id] = relation;
                AddIfWithinBound(relation.MediaTitleId, titleIds, nextFrontier);
                AddIfWithinBound(relation.RelatedMediaTitleId, titleIds, nextFrontier);
            }

            nextFrontier.ExceptWith(frontier);
            frontier = nextFrontier;
        }

        var traversalIsComplete = frontier.Count == 0;

        // Traverse only structural franchise relations. Generic CHARACTER and
        // OTHER nodes can be shared by unrelated series, so they are included
        // one hop from the structural component but are never traversal bridges.
        var continuityIdSet = !hasStructuralRootRelation && titleIds.Count > 1
            ? titleIds.Where(id => id != mediaTitleId).ToHashSet()
            : titleIds.ToHashSet();
        var continuityIds = continuityIdSet.ToArray();
        var directRelations = await _dbContext.MediaTitleRelations
            .AsNoTracking()
            .Include(relation => relation.MediaTitle)
            .Include(relation => relation.RelatedMediaTitle)
            .Where(relation => relation.SourceProvider == AniListProvider
                && (continuityIds.Contains(relation.MediaTitleId)
                    || continuityIds.Contains(relation.RelatedMediaTitleId)))
            .OrderBy(relation => relation.Id)
            .ToListAsync(cancellationToken);
        foreach (var relation in directRelations)
        {
            if (FranchiseTraversalRelationTypes.Contains(relation.RelationType)
                && (!continuityIdSet.Contains(relation.MediaTitleId)
                    || !continuityIdSet.Contains(relation.RelatedMediaTitleId)))
            {
                continue;
            }

            var missingEndpointCount = (titleIds.Contains(relation.MediaTitleId) ? 0 : 1)
                + (titleIds.Contains(relation.RelatedMediaTitleId) ? 0 : 1);
            if (titleIds.Count + missingEndpointCount > MaxNodes)
            {
                continue;
            }

            titleIds.Add(relation.MediaTitleId);
            titleIds.Add(relation.RelatedMediaTitleId);
            relationsById[relation.Id] = relation;
        }

        var selectedIds = titleIds.ToArray();
        var titles = await _dbContext.MediaTitles
            .AsNoTracking()
            .Where(title => selectedIds.Contains(title.Id))
            .ToDictionaryAsync(title => title.Id, cancellationToken);
        var links = await _dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => selectedIds.Contains(link.MediaTitleId) && link.Provider == AniListProvider)
            .ToDictionaryAsync(link => link.MediaTitleId, cancellationToken);
        var libraryEntries = userId is null
            ? []
            : await _dbContext.MediaLibraryEntries
                .AsNoTracking()
                .Where(entry => entry.UserId == userId.Value && selectedIds.Contains(entry.MediaTitleId))
                .ToListAsync(cancellationToken);
        var libraryEntryByTitle = libraryEntries
            .GroupBy(entry => entry.MediaTitleId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(entry => entry.UpdatedAt).First());

        var graphRelations = relationsById.Values
            .Where(relation => titles.ContainsKey(relation.MediaTitleId)
                && titles.ContainsKey(relation.RelatedMediaTitleId))
            .OrderBy(relation => relation.RelationType, StringComparer.Ordinal)
            .ThenBy(relation => relation.MediaTitleId)
            .ThenBy(relation => relation.RelatedMediaTitleId)
            .ToList();
        var currentSnapshotId = links.GetValueOrDefault(mediaTitleId)?.RelationsSnapshotId;
        var verifiedRelationSourceIds = currentSnapshotId is null
            ? []
            : links
                .Where(item => item.Value.RelationsLastVerifiedAt is not null
                    && item.Value.RelationsSnapshotId == currentSnapshotId)
                .Select(item => item.Key)
                .ToHashSet();
        var continuity = BuildContinuity(
            mediaTitleId,
            titles,
            graphRelations,
            verifiedRelationSourceIds,
            traversalIsComplete);

        var nodes = titles.Values
            .Where(title => links.ContainsKey(title.Id) || title.Id == mediaTitleId)
            .OrderBy(title => title.StartYear ?? int.MaxValue)
            .ThenBy(title => title.CanonicalTitle, StringComparer.OrdinalIgnoreCase)
            .Select(title =>
            {
                links.TryGetValue(title.Id, out var link);
                libraryEntryByTitle.TryGetValue(title.Id, out var entry);
                return new MediaFranchiseNodeDto
                {
                    MediaTitleId = title.Id,
                    Provider = link?.Provider ?? AniListProvider,
                    ProviderMediaId = link?.ExternalId ?? string.Empty,
                    ExternalUrl = link?.ExternalUrl,
                    CanonicalTitle = title.CanonicalTitle,
                    OriginalTitle = title.OriginalTitle,
                    PosterUrl = title.PosterUrl,
                    BackgroundUrl = title.BackgroundUrl,
                    MediaKind = title.MediaKind,
                    MediaFormat = title.Format,
                    StartYear = title.StartYear,
                    EpisodeCount = title.EpisodeCount,
                    IsInLibrary = entry is not null,
                    ViewerStatus = entry?.Status,
                    ProgressEpisodes = entry?.ProgressEpisodes,
                    IsCurrent = title.Id == mediaTitleId
                };
            })
            .ToList();

        return new MediaFranchiseGraphDto
        {
            CurrentMediaTitleId = mediaTitleId,
            SourceProvider = AniListProvider,
            RefreshedAt = links.GetValueOrDefault(mediaTitleId)?.RelationsLastVerifiedAt,
            Nodes = nodes,
            Relations = graphRelations.Select(relation => new MediaFranchiseRelationDto
            {
                SourceMediaTitleId = relation.MediaTitleId,
                TargetMediaTitleId = relation.RelatedMediaTitleId,
                RelationType = relation.RelationType,
                IsEpisodeContinuity = IsContinuityRelation(relation, titles)
            }).ToList(),
            Continuity = continuity
        };
    }

    private static void AddIfWithinBound(Guid id, HashSet<Guid> titleIds, HashSet<Guid> nextFrontier)
    {
        if (titleIds.Contains(id) || titleIds.Count >= MaxNodes)
        {
            return;
        }

        titleIds.Add(id);
        nextFrontier.Add(id);
    }

    private static MediaEpisodeContinuityDto BuildContinuity(
        Guid currentTitleId,
        IReadOnlyDictionary<Guid, MediaTitle> titles,
        IReadOnlyCollection<MediaTitleRelation> relations,
        IReadOnlySet<Guid> verifiedRelationSourceIds,
        bool traversalIsComplete)
    {
        var chronologyEdges = relations
            .Where(relation => IsAnimeChronologyRelation(relation, titles))
            .Select(relation => string.Equals(relation.RelationType, MediaRelationTypes.Prequel, StringComparison.Ordinal)
                ? (Earlier: relation.RelatedMediaTitleId, Later: relation.MediaTitleId)
                : (Earlier: relation.MediaTitleId, Later: relation.RelatedMediaTitleId))
            .Distinct()
            .ToList();
        var directedEdges = BuildEpisodeSeriesEdges(chronologyEdges, titles);
        var episodeSeriesIds = chronologyEdges
            .SelectMany(edge => new[] { edge.Earlier, edge.Later })
            .Where(id => titles.TryGetValue(id, out var title) && IsEpisodeSeriesTitle(title))
            .ToHashSet();
        var connectedIds = directedEdges.Count > 0
            ? SelectContinuityComponent(currentTitleId, directedEdges, titles)
            : episodeSeriesIds.Count > 0
                ? episodeSeriesIds
                : new HashSet<Guid> { currentTitleId };
        var continuityAnchorId = SelectContinuityStart(connectedIds, directedEdges, titles);

        directedEdges = directedEdges
            .Where(edge => connectedIds.Contains(edge.Earlier) && connectedIds.Contains(edge.Later))
            .ToList();
        var previousByNode = directedEdges
            .GroupBy(edge => edge.Later)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Earlier).Distinct().ToList());
        var nextByNode = directedEdges
            .GroupBy(edge => edge.Earlier)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Later).Distinct().ToList());
        var isComplete = traversalIsComplete
            && connectedIds.All(verifiedRelationSourceIds.Contains)
            && previousByNode.Values.All(values => values.Count <= 1)
            && nextByNode.Values.All(values => values.Count <= 1);

        var before = new List<Guid>();
        var seen = new HashSet<Guid> { continuityAnchorId };
        var cursor = continuityAnchorId;
        while (previousByNode.TryGetValue(cursor, out var previous) && previous.Count == 1)
        {
            cursor = previous[0];
            if (!seen.Add(cursor))
            {
                isComplete = false;
                break;
            }

            before.Add(cursor);
        }
        before.Reverse();

        var ordered = new List<Guid>(before) { continuityAnchorId };
        cursor = continuityAnchorId;
        while (nextByNode.TryGetValue(cursor, out var next) && next.Count == 1)
        {
            cursor = next[0];
            if (!seen.Add(cursor))
            {
                isComplete = false;
                break;
            }

            ordered.Add(cursor);
        }

        if (seen.Count != connectedIds.Count)
        {
            isComplete = false;
        }

        var offsets = new Dictionary<Guid, int>();
        var offset = 0;
        var countsAreComplete = true;
        foreach (var titleId in ordered)
        {
            if (countsAreComplete)
            {
                offsets[titleId] = offset;
            }

            if (!titles.TryGetValue(titleId, out var title) || title.EpisodeCount is not > 0)
            {
                countsAreComplete = false;
                isComplete = false;
                continue;
            }

            if (countsAreComplete)
            {
                offset += title.EpisodeCount.Value;
            }
        }

        return new MediaEpisodeContinuityDto
        {
            OrderedMediaTitleIds = ordered,
            EpisodeOffsetByMediaTitleId = offsets,
            IsComplete = isComplete
        };
    }

    private static bool IsAnimeChronologyRelation(
        MediaTitleRelation relation,
        IReadOnlyDictionary<Guid, MediaTitle> titles)
    {
        if (!string.Equals(relation.RelationType, MediaRelationTypes.Prequel, StringComparison.Ordinal)
            && !string.Equals(relation.RelationType, MediaRelationTypes.Sequel, StringComparison.Ordinal))
        {
            return false;
        }

        return titles.TryGetValue(relation.MediaTitleId, out var source)
            && titles.TryGetValue(relation.RelatedMediaTitleId, out var target)
            && string.Equals(source.MediaKind, MediaKinds.Anime, StringComparison.Ordinal)
            && string.Equals(target.MediaKind, MediaKinds.Anime, StringComparison.Ordinal);
    }

    private static bool IsContinuityRelation(
        MediaTitleRelation relation,
        IReadOnlyDictionary<Guid, MediaTitle> titles)
    {
        return IsAnimeChronologyRelation(relation, titles)
            && titles.TryGetValue(relation.MediaTitleId, out var source)
            && titles.TryGetValue(relation.RelatedMediaTitleId, out var target)
            && IsEpisodeSeriesTitle(source)
            && IsEpisodeSeriesTitle(target);
    }

    private static List<(Guid Earlier, Guid Later)> BuildEpisodeSeriesEdges(
        IReadOnlyCollection<(Guid Earlier, Guid Later)> chronologyEdges,
        IReadOnlyDictionary<Guid, MediaTitle> titles)
    {
        var outgoing = chronologyEdges
            .GroupBy(edge => edge.Earlier)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.Later).Distinct().ToList());
        var seriesIds = chronologyEdges
            .SelectMany(edge => new[] { edge.Earlier, edge.Later })
            .Where(id => titles.TryGetValue(id, out var title) && IsEpisodeSeriesTitle(title))
            .Distinct()
            .ToList();
        var result = new HashSet<(Guid Earlier, Guid Later)>();

        foreach (var seriesId in seriesIds)
        {
            var pending = new Queue<Guid>(outgoing.GetValueOrDefault(seriesId) ?? []);
            var visited = new HashSet<Guid> { seriesId };
            while (pending.TryDequeue(out var candidate))
            {
                if (!visited.Add(candidate))
                {
                    continue;
                }

                if (titles.TryGetValue(candidate, out var title) && IsEpisodeSeriesTitle(title))
                {
                    result.Add((seriesId, candidate));
                    continue;
                }

                foreach (var next in outgoing.GetValueOrDefault(candidate) ?? [])
                {
                    pending.Enqueue(next);
                }
            }
        }

        return result.ToList();
    }

    private static bool IsEpisodeSeriesTitle(MediaTitle title)
    {
        return string.Equals(title.Format, MediaFormats.Tv, StringComparison.OrdinalIgnoreCase)
            || string.Equals(title.Format, MediaFormats.TvShort, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<Guid> SelectContinuityComponent(
        Guid currentTitleId,
        IReadOnlyCollection<(Guid Earlier, Guid Later)> edges,
        IReadOnlyDictionary<Guid, MediaTitle> titles)
    {
        var remaining = edges
            .SelectMany(edge => new[] { edge.Earlier, edge.Later })
            .ToHashSet();
        if (remaining.Count == 0)
        {
            return [currentTitleId];
        }

        var components = new List<HashSet<Guid>>();
        while (remaining.Count > 0)
        {
            var component = new HashSet<Guid> { remaining.First() };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in edges)
                {
                    if (component.Contains(edge.Earlier) && component.Add(edge.Later)
                        || component.Contains(edge.Later) && component.Add(edge.Earlier))
                    {
                        changed = true;
                    }
                }
            }

            remaining.ExceptWith(component);
            components.Add(component);
        }

        return components
            .OrderByDescending(component => component.Count)
            .ThenBy(component => component.Min(id => titles.GetValueOrDefault(id)?.StartYear ?? int.MaxValue))
            .ThenBy(component => component.Min())
            .First();
    }

    private static Guid SelectContinuityStart(
        IReadOnlySet<Guid> component,
        IReadOnlyCollection<(Guid Earlier, Guid Later)> edges,
        IReadOnlyDictionary<Guid, MediaTitle> titles)
    {
        var laterIds = edges
            .Where(edge => component.Contains(edge.Earlier) && component.Contains(edge.Later))
            .Select(edge => edge.Later)
            .ToHashSet();
        return component
            .OrderBy(id => laterIds.Contains(id))
            .ThenBy(id => titles.GetValueOrDefault(id)?.StartYear ?? int.MaxValue)
            .ThenBy(id => titles.GetValueOrDefault(id)?.CanonicalTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id)
            .First();
    }

}
