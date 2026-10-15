using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static class MediaCanonicalMetadataPolicy
{
    public static void FillMissing(MediaTitle title, MediaProviderTitleDetails details, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title.CanonicalTitle))
        {
            title.CanonicalTitle = details.Title;
        }
        if (string.IsNullOrWhiteSpace(title.SortTitle)) title.SortTitle = details.Title;
        if (string.IsNullOrWhiteSpace(title.OriginalTitle)) title.OriginalTitle = details.NativeTitle;
        if (title.Synonyms.Count == 0 && details.Synonyms.Count > 0)
        {
            title.Synonyms = [.. details.Synonyms];
        }
        if (string.IsNullOrWhiteSpace(title.Synopsis)) title.Synopsis = details.Synopsis;
        if (string.IsNullOrWhiteSpace(title.Format)) title.Format = details.Format;
        if (string.IsNullOrWhiteSpace(title.PosterUrl)) title.PosterUrl = details.PosterUrl;
        if (string.IsNullOrWhiteSpace(title.BackgroundUrl)) title.BackgroundUrl = details.BackgroundUrl;
        title.StartYear ??= details.StartYear;
        title.EpisodeCount ??= details.EpisodeCount;
        title.ChapterCount ??= details.ChapterCount;
        title.VolumeCount ??= details.VolumeCount;
        title.ReleasedCount ??= details.ReleasedCount;
        title.TotalKnownCount ??= details.TotalKnownCount;
        if (details.NextReleaseAt is not null)
        {
            title.NextReleaseAt ??= details.NextReleaseAt;
            if (string.IsNullOrWhiteSpace(title.NextReleaseLabel)) title.NextReleaseLabel = details.NextReleaseLabel;
        }
        if (string.IsNullOrWhiteSpace(title.PrimaryProgressDimension))
        {
            title.PrimaryProgressDimension = details.PrimaryProgressDimension;
        }
        if (string.IsNullOrWhiteSpace(title.ReleaseStatusDimension))
        {
            title.ReleaseStatusDimension = details.ReleaseStatusDimension;
        }
        title.UpdatedAt = now;
    }

    public static bool ShouldApply(
        string incomingProviderId,
        IEnumerable<string> existingProviderIds)
    {
        var normalizedIncomingProviderId = NormalizeProviderId(incomingProviderId);
        return normalizedIncomingProviderId is not (MediaObservationSiteIdentifiers.MyAnimeList or MediaObservationSiteIdentifiers.Simkl)
            || !existingProviderIds.Any(providerId =>
                NormalizeProviderId(providerId) == MediaObservationSiteIdentifiers.AniList);
    }

    private static string NormalizeProviderId(string providerId)
        => providerId.Trim().ToLowerInvariant();
}
