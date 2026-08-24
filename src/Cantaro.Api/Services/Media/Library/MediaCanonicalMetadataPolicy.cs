using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static class MediaCanonicalMetadataPolicy
{
    public static bool ShouldApply(
        string incomingProviderId,
        IEnumerable<string> existingProviderIds)
    {
        var normalizedIncomingProviderId = NormalizeProviderId(incomingProviderId);
        return normalizedIncomingProviderId != MediaObservationSiteIdentifiers.MyAnimeList
            || !existingProviderIds.Any(providerId =>
                NormalizeProviderId(providerId) == MediaObservationSiteIdentifiers.AniList);
    }

    private static string NormalizeProviderId(string providerId)
        => providerId.Trim().ToLowerInvariant();
}
