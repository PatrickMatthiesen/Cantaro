using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Xunit;

namespace Cantaro.Api.Tests.Media.Library;

public class MediaCanonicalMetadataFillMissingTests
{
    [Fact]
    public void FillMissingAddsProviderDetailsWhenAniListRemainsMetadataAuthority()
    {
        var now = DateTimeOffset.Parse("2026-10-15T12:00:00Z");
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "AniList title",
            MediaKind = "anime",
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1),
            ProviderLinks = [new MediaProviderLink
            {
                Provider = "anilist",
                ExternalId = "123",
                LinkSource = "imported",
            }],
        };
        var details = new MediaProviderTitleDetails
        {
            ProviderId = "simkl",
            ProviderMediaId = "anime:456",
            Title = "SIMKL title",
            NativeTitle = "Native title",
            Synonyms = ["Alternate title"],
            MediaKind = "anime",
            Synopsis = "A synopsis",
            Format = "tv",
            PosterUrl = "https://example.test/poster.jpg",
            BackgroundUrl = "https://example.test/background.jpg",
            StartYear = 2026,
            EpisodeCount = 12,
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
        };

        Assert.False(MediaCanonicalMetadataPolicy.ShouldApply("simkl", ["anilist"]));
        MediaCanonicalMetadataPolicy.FillMissing(title, details, now);

        Assert.Equal("AniList title", title.CanonicalTitle);
        Assert.Equal("SIMKL title", title.SortTitle);
        Assert.Equal("Native title", title.OriginalTitle);
        Assert.Equal(["Alternate title"], title.Synonyms);
        Assert.Equal("A synopsis", title.Synopsis);
        Assert.Equal("tv", title.Format);
        Assert.Equal("https://example.test/poster.jpg", title.PosterUrl);
        Assert.Equal("https://example.test/background.jpg", title.BackgroundUrl);
        Assert.Equal(2026, title.StartYear);
        Assert.Equal(12, title.EpisodeCount);
        Assert.Null(title.NextReleaseAt);
        Assert.Null(title.NextReleaseLabel);
        Assert.Equal(now, title.UpdatedAt);
    }

    [Fact]
    public void FillMissingPreservesPopulatedMetadataAndDoesNotClearKnownRelease()
    {
        var now = DateTimeOffset.Parse("2026-10-15T12:00:00Z");
        var release = now.AddDays(3);
        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = "Canonical title",
            SortTitle = "Existing sort title",
            OriginalTitle = "Existing original title",
            Synonyms = ["Existing synonym"],
            MediaKind = "anime",
            Synopsis = "Existing synopsis",
            Format = "movie",
            PosterUrl = "https://example.test/existing-poster.jpg",
            BackgroundUrl = "https://example.test/existing-background.jpg",
            StartYear = 2024,
            EpisodeCount = 24,
            ReleasedCount = 8,
            TotalKnownCount = 24,
            NextReleaseAt = release,
            NextReleaseLabel = "Existing release label",
            PrimaryProgressDimension = "episode",
            ReleaseStatusDimension = "episode",
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1),
        };
        var details = new MediaProviderTitleDetails
        {
            ProviderId = "simkl",
            ProviderMediaId = "anime:456",
            Title = "Different provider title",
            NativeTitle = "Different native title",
            Synonyms = ["Different synonym"],
            MediaKind = "anime",
            Synopsis = "Different synopsis",
            Format = "tv",
            PosterUrl = "https://example.test/new-poster.jpg",
            BackgroundUrl = "https://example.test/new-background.jpg",
            StartYear = 2026,
            EpisodeCount = 12,
            ReleasedCount = 0,
            TotalKnownCount = 12,
            NextReleaseAt = null,
            NextReleaseLabel = null,
            PrimaryProgressDimension = "completion",
            ReleaseStatusDimension = "completion",
        };

        MediaCanonicalMetadataPolicy.FillMissing(title, details, now);

        Assert.Equal("Canonical title", title.CanonicalTitle);
        Assert.Equal("Existing sort title", title.SortTitle);
        Assert.Equal("Existing original title", title.OriginalTitle);
        Assert.Equal(["Existing synonym"], title.Synonyms);
        Assert.Equal("Existing synopsis", title.Synopsis);
        Assert.Equal("movie", title.Format);
        Assert.Equal("https://example.test/existing-poster.jpg", title.PosterUrl);
        Assert.Equal("https://example.test/existing-background.jpg", title.BackgroundUrl);
        Assert.Equal(2024, title.StartYear);
        Assert.Equal(24, title.EpisodeCount);
        Assert.Equal(8, title.ReleasedCount);
        Assert.Equal(24, title.TotalKnownCount);
        Assert.Equal(release, title.NextReleaseAt);
        Assert.Equal("Existing release label", title.NextReleaseLabel);
        Assert.Equal("episode", title.PrimaryProgressDimension);
        Assert.Equal("episode", title.ReleaseStatusDimension);
        Assert.Equal(now, title.UpdatedAt);
    }
}
