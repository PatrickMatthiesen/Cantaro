namespace Cantaro.Api.Services;

public static class TrackObservationDisplayFormatter
{
    private static readonly string[] ArtistTitleSeparators = [" - ", " \u2013 ", " \u2014 "];

    public static string GetQueueTitle(Models.TrackObservation observation, TrackObservationMetadata? metadata)
    {
        var preferredTitle = metadata?.OriginalTitle;
        if (string.IsNullOrWhiteSpace(preferredTitle))
        {
            return observation.Title;
        }

        var artistCandidates = new[]
        {
            observation.Artist,
            metadata?.Artist,
            metadata?.SearchArtist
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => NormalizeComparisonValue(value!))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var trimmedTitle = preferredTitle.Trim();
        foreach (var separator in ArtistTitleSeparators)
        {
            var separatorIndex = trimmedTitle.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var titlePrefix = NormalizeComparisonValue(trimmedTitle[..separatorIndex]);
            if (!artistCandidates.Contains(titlePrefix, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var titleSuffix = trimmedTitle[(separatorIndex + separator.Length)..].Trim();
            return string.IsNullOrWhiteSpace(titleSuffix) ? observation.Title : titleSuffix;
        }

        return trimmedTitle;
    }

    public static string? GetQueueArtist(Models.TrackObservation observation, TrackObservationMetadata? metadata)
    {
        return !string.IsNullOrWhiteSpace(metadata?.SearchArtist)
            ? metadata.SearchArtist
            : observation.Artist ?? metadata?.Artist;
    }

    private static string NormalizeComparisonValue(string value) => string.Join(
        " ",
        value
            .Trim()
            .Replace("\u2013", "-", StringComparison.Ordinal)
            .Replace("\u2014", "-", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
