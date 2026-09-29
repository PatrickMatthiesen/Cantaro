using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed record PlaylistSyncItem(Guid? TrackId, Guid? ObservationId, string? ExternalId, string? Service)
{
    public string Identity => TrackId is { } track ? $"track:{track}" : ObservationId is { } observation
        ? $"observation:{observation}" : $"{Service}:{ExternalId}";
    public static PlaylistSyncItem From(PlaylistEntry entry) => new(
        entry.TrackId, entry.TrackObservationId, entry.TrackObservation?.ExternalId,
        entry.TrackObservation?.SourceType ?? entry.SourceService);
    public static PlaylistSyncItem From(PlaylistRemoteTrack track, string service) => new(
        track.TrackId, track.ObservationId, track.ExternalId, service);
}

public sealed record PlaylistSyncBaseline(List<PlaylistSyncItem> Items, string? ProviderRevision = null)
{
    public static PlaylistSyncBaseline? Parse(string? json) => string.IsNullOrWhiteSpace(json)
        ? null : JsonSerializer.Deserialize<PlaylistSyncBaseline>(json);
    public string Serialize() => JsonSerializer.Serialize(this);
}

public sealed record PlaylistMergeResult(List<PlaylistSyncItem> Items, bool OrderConflict);

/// <summary>Compares changes against one link's acknowledged contents, including repeated occurrences.</summary>
public static class PlaylistSyncMerge
{
    public static List<PlaylistSyncItem> Normalize(IEnumerable<PlaylistSyncItem> items, bool allowDuplicates)
        => allowDuplicates ? items.ToList() : items.DistinctBy(item => item.Identity).ToList();

    public static PlaylistMergeResult Merge(
        IReadOnlyList<PlaylistSyncItem> current, IReadOnlyList<PlaylistSyncItem>? baseline,
        IReadOnlyList<PlaylistSyncItem> remote, bool allowDuplicates)
    {
        var local = Tokens(Normalize(current, allowDuplicates));
        var incoming = Tokens(Normalize(remote, allowDuplicates));
        if (baseline is null)
        {
            // Preserve existing local order, but place new remote occurrences alongside
            // their remote neighbors instead of appending them all to the end.
            // Without a shared anchor, the two lists retain their separate order.
            var firstShared = incoming.FindIndex(item => local.Any(old => old.Key == item.Key));
            var insertionIndex = firstShared < 0 ? local.Count
                : local.FindIndex(item => item.Key == incoming[firstShared].Key);
            foreach (var item in incoming)
            {
                var existingIndex = local.FindIndex(old => old.Key == item.Key);
                if (existingIndex >= 0) insertionIndex = existingIndex + 1;
                else local.Insert(insertionIndex++, item);
            }
            return new(local.Select(item => item.Item).ToList(), false);
        }

        var previous = Tokens(Normalize(baseline, allowDuplicates));
        var oldKeys = previous.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var remoteKeys = incoming.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var removed = oldKeys.Except(remoteKeys).ToHashSet(StringComparer.Ordinal);
        local.RemoveAll(item => removed.Contains(item.Key));
        var common = oldKeys.Intersect(remoteKeys).Intersect(local.Select(item => item.Key)).ToHashSet();
        var previousOrder = previous.Where(item => common.Contains(item.Key)).Select(item => item.Key).ToList();
        var remoteOrder = incoming.Where(item => common.Contains(item.Key)).Select(item => item.Key).ToList();
        var localOrder = local.Where(item => common.Contains(item.Key)).Select(item => item.Key).ToList();
        var remoteReordered = !previousOrder.SequenceEqual(remoteOrder);
        var conflict = remoteReordered && !previousOrder.SequenceEqual(localOrder) && !remoteOrder.SequenceEqual(localOrder);
        if (remoteReordered && !conflict)
        {
            var ordered = incoming.Where(item => common.Contains(item.Key)).GetEnumerator();
            for (var index = 0; index < local.Count; index++)
                if (common.Contains(local[index].Key) && ordered.MoveNext()) local[index] = ordered.Current;
        }

        for (var index = 0; index < incoming.Count; index++)
        {
            var addition = incoming[index];
            if (oldKeys.Contains(addition.Key) || local.Any(item => item.Key == addition.Key)) continue;
            var predecessor = incoming.Take(index).Reverse()
                .Select(item => local.FindIndex(candidate => candidate.Key == item.Key)).FirstOrDefault(position => position >= 0, -1);
            local.Insert(predecessor < 0 ? 0 : predecessor + 1, addition);
        }
        var observed = incoming.ToDictionary(item => item.Key, item => item.Item);
        return new(local.Select(item => observed.GetValueOrDefault(item.Key, item.Item)).ToList(), conflict);
    }

    private static List<(string Key, PlaylistSyncItem Item)> Tokens(IEnumerable<PlaylistSyncItem> items)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        return items.Select(item =>
        {
            var count = counts.GetValueOrDefault(item.Identity) + 1;
            counts[item.Identity] = count;
            return ($"{item.Identity}#{count}", item);
        }).ToList();
    }
}
