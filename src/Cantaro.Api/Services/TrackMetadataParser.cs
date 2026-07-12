using System.Text.RegularExpressions;

namespace Cantaro.Api.Services;

public sealed class ParsedTrackMetadata
{
    public required string DisplayTitle { get; init; }
    public string? DisplayArtist { get; init; }
    public required string SearchTitle { get; init; }
    public string? SearchArtist { get; init; }
    public bool ParsedArtistFromTitle { get; init; }
    public IReadOnlyList<string> ArtistCredits { get; init; } = [];
    public IReadOnlyList<string> VersionMarkers { get; init; } = [];
    public IReadOnlyList<string> PlaybackModifiers { get; init; } = [];
    public IReadOnlyList<string> PresentationMarkers { get; init; } = [];
}

internal sealed class ParsedTitleSemantics
{
    public IReadOnlyList<string> VersionMarkers { get; init; } = [];
    public IReadOnlyList<string> PlaybackModifiers { get; init; } = [];
    public IReadOnlyList<string> PresentationMarkers { get; init; } = [];
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
        var featuredArtists = ExtractFeaturedArtistNames(rawTitle);

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

        var baseDisplayArtist = parsedArtist ?? cleanedArtist;
        var displayArtist = AppendFeaturedArtists(baseDisplayArtist, featuredArtists);
        var searchTitle = CleanupWhitespace(StripFeaturedArtists(displayTitle) ?? displayTitle);
        if (string.IsNullOrWhiteSpace(searchTitle))
        {
            searchTitle = displayTitle;
        }

        var searchArtist = CleanupArtist(displayArtist);
        var artistCredits = BuildObservationArtistCredits(baseDisplayArtist, featuredArtists);

        return new ParsedTrackMetadata
        {
            DisplayTitle = displayTitle,
            DisplayArtist = displayArtist,
            SearchTitle = searchTitle,
            SearchArtist = searchArtist,
            ParsedArtistFromTitle = parsedArtist != null,
            ArtistCredits = artistCredits,
            VersionMarkers = titleSemantics.VersionMarkers,
            PlaybackModifiers = titleSemantics.PlaybackModifiers,
            PresentationMarkers = titleSemantics.PresentationMarkers
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
            PlaybackModifiers = ExtractMarkers(normalizedValue, PlaybackModifierRegex()),
            PresentationMarkers = ExtractMarkers(normalizedValue, PresentationMarkerRegex())
        };
    }

    internal static IReadOnlyList<string> NormalizeArtistCredits(IEnumerable<string?> credits)
    {
        return credits
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TrackTextNormalizer.Normalize(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool HaveEquivalentArtistCredits(
        ParsedTrackMetadata observation,
        ParsedTrackMetadata candidate,
        IEnumerable<string?> candidateCredits)
    {
        var normalizedCandidateCredits = NormalizeArtistCredits(candidateCredits);
        if (normalizedCandidateCredits.Count == 0)
        {
            normalizedCandidateCredits = candidate.ArtistCredits;
        }

        if (observation.ArtistCredits.Count > 0
            && observation.ArtistCredits.SequenceEqual(normalizedCandidateCredits, StringComparer.Ordinal))
        {
            return true;
        }

        var observationArtist = TrackTextNormalizer.Normalize(observation.DisplayArtist ?? observation.SearchArtist);
        var candidateArtist = TrackTextNormalizer.Normalize(candidate.DisplayArtist ?? candidate.SearchArtist);
        return !string.IsNullOrWhiteSpace(observationArtist)
            && string.Equals(observationArtist, candidateArtist, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> BuildObservationArtistCredits(
        string? artist,
        IReadOnlyList<string> featuredArtists)
    {
        var credits = SplitXCollaborators(StripFeaturedArtists(artist)).Cast<string?>().ToList();
        credits.AddRange(featuredArtists);

        return NormalizeArtistCredits(credits);
    }

    private static IReadOnlyList<string> SplitXCollaborators(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return [];
        }

        return SpacedXCollaboratorRegex().Split(artist)
            .Select(CleanupArtist)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractFeaturedArtistNames(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        return FeaturedArtistCaptureRegex().Matches(title)
            .SelectMany(match => match.Groups["artists"].Value
                .Split([",", " & ", " and "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(CleanupArtist)
            .Where(artist => !string.IsNullOrWhiteSpace(artist))
            .Select(artist => artist!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? AppendFeaturedArtists(string? artist, IReadOnlyList<string> featuredArtists)
    {
        if (featuredArtists.Count == 0)
        {
            return artist;
        }

        var additions = featuredArtists
            .Where(featured => string.IsNullOrWhiteSpace(artist)
                || !TrackTextNormalizer.Normalize(artist).Contains(
                    TrackTextNormalizer.Normalize(featured),
                    StringComparison.Ordinal))
            .ToArray();
        if (additions.Length == 0)
        {
            return artist;
        }

        return string.IsNullOrWhiteSpace(artist)
            ? string.Join(", ", additions)
            : $"{artist}, {string.Join(", ", additions)}";
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
        var marker = CleanupWhitespace(value.ToLowerInvariant());
        if (marker.Contains("remaster", StringComparison.Ordinal)) return "remaster";
        if (marker.Contains("radio", StringComparison.Ordinal)) return "radio-edit";
        if (marker.Contains("vip", StringComparison.Ordinal)) return "vip-mix";
        if (marker.Contains("extended", StringComparison.Ordinal)) return "extended-mix";
        if (marker.Contains("club", StringComparison.Ordinal)) return "club-mix";
        if (marker.Contains("original", StringComparison.Ordinal)) return "original-mix";
        if (marker.Contains("remix", StringComparison.Ordinal)) return "remix";
        if (marker.Contains("official", StringComparison.Ordinal) && marker.Contains("video", StringComparison.Ordinal)) return "official-video";
        if (marker.Contains("lyric", StringComparison.Ordinal) && marker.Contains("video", StringComparison.Ordinal)) return "lyric-video";
        return marker;
    }

    [GeneratedRegex(@"[\[(](?<text>.*?)[\])]")]
    private static partial Regex BracketedSegmentRegex();

    [GeneratedRegex(@"\b(official|video|audio|lyrics|lyric|visualizer|hq|hd|copyright\s*free|future\s*bass|ncs)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseContentRegex();

    [GeneratedRegex(@"\b(speed\s*up|sped\s*up|nightcore|slowed(?:\s*\+\s*reverb)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlaybackModifierRegex();

    [GeneratedRegex(@"\b(intro\s+dirty|acoustic|live|remix(?:ed)?|remaster(?:ed)?|instrumental|karaoke|demo|dirty|clean|intro|outro|radio(?:\s+(?:edit|version))?|vip\s+mix|extended(?:\s+mix)?|club\s+mix|original\s+mix|acapella|stripped|cover|edit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionMarkerRegex();

    [GeneratedRegex(@"\b(official\s+(?:music\s+)?video|lyric(?:s)?\s+video|visualizer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PresentationMarkerRegex();

    [GeneratedRegex(@"(?:feat|ft|featuring)\.?\s+(?<artists>.*?)(?=\s+-\s+|[\[\]\(\)\|]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedArtistCaptureRegex();

    [GeneratedRegex(@"\s+[x×]\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SpacedXCollaboratorRegex();

    [GeneratedRegex(@"[\[(]\s*(feat|ft|featuring)\.?\s+[^\])]*[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedParentheticalRegex();

    [GeneratedRegex(@"\s+\b(feat|ft|featuring)\.?\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturedInlineRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s*-\s*topic$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffixRegex();
}
