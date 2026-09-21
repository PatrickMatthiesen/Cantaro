using System.Globalization;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface IAniListAvailabilityEnricher
{
    Task<IReadOnlyList<MediaProviderAvailabilityLink>?> GetAvailabilityAsync(
        IReadOnlyList<MediaProviderCrossReference> crossReferences,
        CancellationToken cancellationToken);
}

public sealed class AniListAvailabilityEnricher(AniListApiClient apiClient) : IAniListAvailabilityEnricher
{
    public async Task<IReadOnlyList<MediaProviderAvailabilityLink>?> GetAvailabilityAsync(
        IReadOnlyList<MediaProviderCrossReference> crossReferences,
        CancellationToken cancellationToken)
    {
        var aniListId = ParseAniListId(crossReferences);
        var malId = aniListId is null ? ParseMalId(crossReferences) : null;
        if (aniListId is null && malId is null) return null;

        var data = aniListId is { } requestedAniListId
            ? await apiClient.SendPublicGraphQlAsync<AniListMediaData>(
                AvailabilityByAniListIdQuery,
                new { id = requestedAniListId },
                cancellationToken)
            : await apiClient.SendPublicGraphQlAsync<AniListMediaData>(
                AvailabilityByMalIdQuery,
                new { idMal = malId!.Value },
                cancellationToken);
        var media = data.Media;
        if (media is null
            || aniListId is { } expectedAniListId && media.Id != expectedAniListId
            || malId is { } expectedMalId && media.IdMal != expectedMalId)
        {
            throw new InvalidOperationException("AniList did not return the requested anime identity.");
        }

        return AniListMediaProvider.BuildAvailabilityLinks(media);
    }

    private static int? ParseAniListId(IReadOnlyList<MediaProviderCrossReference> crossReferences)
        => ParsePositiveInteger(crossReferences.FirstOrDefault(reference =>
            reference.ProviderId == MediaObservationSiteIdentifiers.AniList)?.ProviderMediaId);

    private static int? ParseMalId(IReadOnlyList<MediaProviderCrossReference> crossReferences)
    {
        var providerMediaId = crossReferences.FirstOrDefault(reference =>
            reference.ProviderId == MediaObservationSiteIdentifiers.MyAnimeList)?.ProviderMediaId;
        return providerMediaId?.StartsWith("anime:", StringComparison.Ordinal) == true
            ? ParsePositiveInteger(providerMediaId["anime:".Length..])
            : null;
    }

    private static int? ParsePositiveInteger(string? value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private const string AvailabilityByAniListIdQuery = """
        query ($id: Int!) {
          Media(id: $id, type: ANIME) {
            id
            idMal
            externalLinks {
              url
              site
              type
              language
              icon
            }
            streamingEpisodes {
              title
              url
              site
            }
          }
        }
        """;

    private const string AvailabilityByMalIdQuery = """
        query ($idMal: Int!) {
          Media(idMal: $idMal, type: ANIME) {
            id
            idMal
            externalLinks {
              url
              site
              type
              language
              icon
            }
            streamingEpisodes {
              title
              url
              site
            }
          }
        }
        """;
}
