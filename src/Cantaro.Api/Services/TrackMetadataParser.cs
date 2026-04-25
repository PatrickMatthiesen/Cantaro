using System.Text.RegularExpressions;

namespace Cantaro.Api.Services;

public sealed class ParsedTrackMetadata
{
    public required string DisplayTitle { get; init; }
    public string? DisplayArtist { get; init; }
    public required string SearchTitle { get; init; }
    public string? SearchArtist { get; init; }
    public bool ParsedArtistFromTitle { get; init; }
    public IReadOnlyList<string> VersionMarkers { get; init; } = [];
    public IReadOnlyList<string> PlaybackModifiers { get; init; } = [];
}

internal sealed class ParsedTitleSemantics
{
    public IReadOnlyList<string> VersionMarkers { get; init; } = [];
    public IReadOnlyList<string> PlaybackModifiers { get; init; } = [];
}

public static partial class TrackMetadataParser
{
    private static readonly string[] ArtistTitleSeparators = [" - ", " \u2013 ", " \u2014 "];

    public static ParsedTrackMetadata Parse(string? rawTitle, string? rawArtist)
    {
        var titleSemantics = ExtractTitleSemantics(rawTitle);
        var cleanedTitle = CleanupTitle(rawTitle);
        var isTopicChannel = IsTopicChannel(rawArtist);
        var cleanedArtist = CleanupArtist(rawArtist);

        var displayTitle = cleanedTitle;
        string? parsedArtist = null;

        foreach (var separator in ArtistTitleSeparators)
        {
            var separatorIndex = cleanedTitle.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var candidateArtist = CleanupArtist(cleanedTitle[..separatorIndex]);
            var candidateTitle = CleanupTitle(cleanedTitle[(separatorIndex + separator.Length)..]);
            if (!IsLikelyArtistSegment(candidateArtist) || string.IsNullOrWhiteSpace(candidateTitle))
            {
                continue;
            }

            parsedArtist = candidateArtist;
            displayTitle = StripTrailingContextSegments(candidateTitle);
            break;
        }

        displayTitle = StripPlaybackModifiers(displayTitle);
        if (parsedArtist == null && isTopicChannel && displayTitle.Contains('|', StringComparison.Ordinal))
        {
            displayTitle = CleanupWhitespace(displayTitle.Split('|', 2, StringSplitOptions.TrimEntries)[0]);
        }

        displayTitle = CleanupWhitespace(displayTitle.Trim(' ', '-', '|'));
        if (string.IsNullOrWhiteSpace(displayTitle))
        {
            displayTitle = cleanedTitle;
        }

        var displayArtist = parsedArtist ?? cleanedArtist;
        var searchTitle = CleanupWhitespace(StripFeaturedArtists(displayTitle) ?? displayTitle);
        if (string.IsNullOrWhiteSpace(searchTitle))
        {
            searchTitle = displayTitle;
        }

        var searchArtist = CleanupArtist(StripFeaturedArtists(displayArtist));

        return new ParsedTrackMetadata
        {
            DisplayTitle = displayTitle,
            DisplayArtist = displayArtist,
            SearchTitle = searchTitle,
            SearchArtist = searchArtist,
            ParsedArtistFromTitle = parsedArtist != null,
            VersionMarkers = titleSemantics.VersionMarkers,
            PlaybackModifiers = titleSemantics.PlaybackModifiers
        };
    }

    internal static ParsedTitleSemantics ExtractTitleSemantics(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ParsedTitleSemantics();
        }

        var normalizedValue = NormalizeDashes(value);
        return new ParsedTitleSemantics
        {
            VersionMarkers = ExtractMarkers(normalizedValue, VersionMarkerRegex()),
            PlaybackModifiers = ExtractMarkers(normalizedValue, PlaybackModifierRegex())
        };
    }

    private static string CleanupTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = NormalizeDashes(value);
        cleaned = StripBracketedNoise(cleaned);
        cleaned = StripPlaybackModifiers(cleaned);
        return CleanupWhitespace(cleaned.Trim(' ', '-', '|'));
    }

    private static string? CleanupArtist(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = NormalizeDashes(value);
        cleaned = TopicSuffixRegex().Replace(cleaned, string.Empty);
        cleaned = StripBracketedNoise(cleaned);
        cleaned = CleanupWhitespace(cleaned.Trim(' ', '-', '|'));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static bool IsTopicChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return TopicSuffixRegex().IsMatch(NormalizeDashes(value));
    }

    private static string NormalizeDashes(string value) => value
        .Replace("\u2013", "-", StringComparison.Ordinal)
        .Replace("\u2014", "-", StringComparison.Ordinal);

    private static string StripBracketedNoise(string value)
    {
        return BracketedSegmentRegex().Replace(value, match =>
        {
            var innerText = match.Groups["text"].Value;
            return NoiseContentRegex().IsMatch(innerText) ? " " : match.Value;
        });
    }

    private static string StripTrailingContextSegments(string value)
    {
        var segments = value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length <= 1)
        {
            return value;
        }

        return segments.Skip(1).Any(segment => NoiseContentRegex().IsMatch(segment))
            ? segments[0]
            : value;
    }

    private static string StripPlaybackModifiers(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return CleanupWhitespace(PlaybackModifierRegex().Replace(value, " "));
    }

    private static string? StripFeaturedArtists(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var cleaned = FeaturedParentheticalRegex().Replace(value, " ");
        cleaned = FeaturedInlineRegex().Replace(cleaned, " ");
        cleaned = CleanupWhitespace(cleaned);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static bool IsLikelyArtistSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('|', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        if (NoiseContentRegex().IsMatch(value))
        {
            return false;
        }

        var wordCount = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        return wordCount is > 0 and <= 6;
    }

    private static string CleanupWhitespace(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    private static IReadOnlyList<string> ExtractMarkers(string value, Regex regex)
    {
        return regex.Matches(value)
            .Select(match => NormalizeMarker(match.Value))
            .Where(match => !string.IsNullOrWhiteSpace(match))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(match => match, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeMarker(string value)
    {
        return CleanupWhitespace(value.ToLowerInvariant());
    }

    [GeneratedRegex(@"[\[(](?<text>.*?)[\])]")]
    private static partial Regex BracketedSegmentRegex();

    [GeneratedRegex(@"\b(official|video|audio|lyrics|lyric|visualizer|hq|hd|copyright\s*free|future\s*bass|ncs|cover)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseContentRegex();

    [GeneratedRegex(@"\b(speed\s*up|sped\s*up|nightcore|slowed(?:\s*\+\s*reverb)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlaybackModifierRegex();

    [GeneratedRegex(@"\b(intro\s+dirty|acoustic|live|remix|remixed|instrumental|karaoke|demo|dirty|clean|intro|outro|radio\s+edit|edit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionMarkerRegex();

    [GeneratedRegex(@"[\[(]\s*(feat|ft|featuring)\.?\s+[^\])]*[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedParentheticalRegex();

    [GeneratedRegex(@"\s+\b(feat|ft|featuring)\.?\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedInlineRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s*-\s*topic$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffixRegex();
}