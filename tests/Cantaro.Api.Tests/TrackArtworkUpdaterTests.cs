using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class TrackArtworkUpdaterTests
{
    [Fact]
    public void FillMissingCanonicalThumbnail_InitializesBlankMetadataAndUpdatesTrack()
    {
        var previousUpdate = new DateTimeOffset(
            2026,
            7,
            28,
            10,
            0,
            0,
            TimeSpan.Zero);
        var track = new Track {
            Id = Guid.NewGuid(),
            UpdatedAt = previousUpdate
         };

        var changed = CatalogFixture.SetArtwork(
            track,
            "https://example.test/art.jpg");

        Assert.True(changed);
        Assert.Equal(previousUpdate, track.UpdatedAt);
        var metadata = JsonSerializer.Deserialize<ProviderCatalogFacts>(
            CatalogFixture.FactsJson(track)!);
        Assert.Equal("https://example.test/art.jpg", metadata?.ThumbnailUrl);
    }
}
