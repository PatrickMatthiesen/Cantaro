using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static class MediaCanonicalMetadataPolicy
{
    public static void Apply(MediaTitle title, MediaProviderTitleDetails details, DateTimeOffset now)
    {
        title.CanonicalTitle = details.Title;
        title.SortTitle = details.Title;
        title.OriginalTitle = details.NativeTitle ?? title.OriginalTitle;
        title.Synonyms = [.. details.Synonyms];
        title.MediaKind = details.MediaKind;
        title.Synopsis = details.Synopsis ?? title.Synopsis;
        title.Format = details.Format ?? title.Format;
        title.PosterUrl = details.PosterUrl ?? title.PosterUrl;
        title.BackgroundUrl = details.BackgroundUrl ?? title.BackgroundUrl;
        title.StartYear = details.StartYear ?? title.StartYear;
        title.EpisodeCount = details.EpisodeCount ?? title.EpisodeCount;
        title.ChapterCount = details.ChapterCount ?? title.ChapterCount;
        title.VolumeCount = details.VolumeCount ?? title.VolumeCount;
        title.ReleasedCount = details.ReleasedCount ?? title.ReleasedCount;
        title.TotalKnownCount = details.TotalKnownCount ?? title.TotalKnownCount;
        title.NextReleaseAt = details.NextReleaseAt;
        title.NextReleaseLabel = details.NextReleaseLabel;
        title.SupportsEpisodeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Episode;
        title.SupportsChapterProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter;
        title.SupportsVolumeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Volume;
        title.IsCompletionOnly = details.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly;
        title.PrimaryProgressDimension = details.PrimaryProgressDimension;
        title.ReleaseStatusDimension = details.ReleaseStatusDimension;
        title.UpdatedAt = now;
    }

    public static void FillMissing(MediaTitle title, MediaProviderTitleDetails details, DateTimeOffset now, bool isSummary = false)
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
        if (isSummary)
        {
            var synonyms = title.Synonyms.Concat(details.Synonyms).Append(details.Title).Append(details.NativeTitle ?? "")
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            if (!title.Synonyms.SequenceEqual(synonyms)) title.Synonyms = synonyms;
            return;
        }
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
