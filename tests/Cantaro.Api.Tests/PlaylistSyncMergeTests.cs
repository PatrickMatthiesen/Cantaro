using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public class PlaylistSyncMergeTests
{
    private static PlaylistSyncItem Item(string id) => new(null, null, id, "test");
    private static List<PlaylistSyncItem> Items(string ids) => ids.Select(id => Item(id.ToString())).ToList();
    private static string Ids(IEnumerable<PlaylistSyncItem> items) => string.Concat(items.Select(item => item.ExternalId));

    [Fact]
    public void RemovalDoesNotEraseIndependentAddition()
    {
        var result = PlaylistSyncMerge.Merge(Items("ABCD"), Items("ABC"), Items("AC"), false);
        Assert.Equal("ACD", Ids(result.Items));
        Assert.False(result.OrderConflict);
    }

    [Fact]
    public void UnexportedItemIsNotADeletion()
    {
        var result = PlaylistSyncMerge.Merge(Items("ABC"), Items("AC"), Items("AC"), false);
        Assert.Equal("ABC", Ids(result.Items));
    }

    [Fact]
    public void MissingBaselineCombinesInsteadOfReplacing()
    {
        Assert.Equal("ACB", Ids(PlaylistSyncMerge.Merge(Items("AB"), null, Items("AC"), false).Items));
    }

    [Theory]
    [InlineData("AB", "XAB", "XAB")]
    [InlineData("AB", "AXB", "AXB")]
    [InlineData("AB", "ABX", "ABX")]
    public void MissingBaselinePlacesRemoteAdditionsInRemoteOrder(string current, string remote, string expected)
    {
        Assert.Equal(expected, Ids(PlaylistSyncMerge.Merge(Items(current), null, Items(remote), false).Items));
    }

    [Fact]
    public void MissingBaselineRetainsLocalOnlyItemsAlongsideRemoteOrder()
    {
        Assert.Equal("AXLB", Ids(PlaylistSyncMerge.Merge(Items("ALB"), null, Items("AXB"), false).Items));
    }

    [Fact]
    public void MissingBaselinePreservesOrNormalizesRepeatedRemoteItemsByDuplicateSetting()
    {
        Assert.Equal("AAB", Ids(PlaylistSyncMerge.Merge(Items("A"), null, Items("AAB"), true).Items));
        Assert.Equal("AB", Ids(PlaylistSyncMerge.Merge(Items("A"), null, Items("AAB"), false).Items));
    }

    [Fact]
    public void MissingBaselineWithDisjointListsKeepsLocalItemsBeforeRemoteItems()
    {
        Assert.Equal("ABXY", Ids(PlaylistSyncMerge.Merge(Items("AB"), null, Items("XY"), false).Items));
    }

    [Fact]
    public void ReorderWithUnchangedLocalIsApplied()
    {
        Assert.Equal("BACD", Ids(PlaylistSyncMerge.Merge(Items("ABCD"), Items("ABC"), Items("BAC"), false).Items));
    }

    [Fact]
    public void IncompatibleOrderingPreservesLocalOrderAndStillAddsNewTracks()
    {
        var result = PlaylistSyncMerge.Merge(Items("ACB"), Items("ABC"), Items("BACD"), false);
        Assert.True(result.OrderConflict);
        Assert.Equal("ACDB", Ids(result.Items));
    }

    [Fact]
    public void DuplicateSettingControlsOccurrenceRemoval()
    {
        Assert.Equal("AB", Ids(PlaylistSyncMerge.Merge(Items("AAB"), Items("AAB"), Items("AB"), true).Items));
        Assert.Equal("AB", Ids(PlaylistSyncMerge.Normalize(Items("AAB"), false)));
        Assert.Equal("AAB", Ids(PlaylistSyncMerge.Normalize(Items("AAB"), true)));
    }

    [Fact]
    public void UnchangedRemoteCannotRestoreLocallyRemovedTrack()
    {
        Assert.Equal("AC", Ids(PlaylistSyncMerge.Merge(Items("AC"), Items("ABC"), Items("ABC"), false).Items));
    }
}
