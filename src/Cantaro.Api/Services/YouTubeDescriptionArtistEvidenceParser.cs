using System.Text.RegularExpressions;

namespace Cantaro.Api.Services;

public sealed record YouTubeDescriptionArtistCredit(
    string Title,
    string Artist,
    string EvidenceLine,
    int LineNumber);

public sealed record YouTubeDescriptionArtistEvidence(
    IReadOnlyList<YouTubeDescriptionArtistCredit> Credits,
    bool HasConflictingAttributions);

/// <summary>
/// Extracts recording-artist evidence only when a description explicitly attributes
/// an exact title anchor. This is evidence for matching, not an identity decision.
/// </summary>
public static class YouTubeDescriptionArtistEvidenceParser
{
    private static readonly Regex TitleSeparatorRegex = new(@"\s+[-–—]\s+", RegexOptions.CultureInvariant);
    private static readonly Regex HeadingArtistRegex = new(
        @"^(?:featured artist|artist|performed by|vocals performed by|cover artist)\s*:\s*(?<artist>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoverArtistHeadingRegex = new(
        @"^cover artist\s*:\s*(?<artist>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ByAttributionRegex = new(
        @"^(?<title>.+?)\s+by\s+(?<artist>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExcludedSectionRegex = new(
        @"^(?:lyrics|tracklist|timestamps|previous\b|related\b|recommended\b|production credits\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExcludedParagraphRegex = new(
        @"^(?:artwork|follow)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExcludedLineRegex = new(
        @"^(?:listen|check out|also|subscribe|support|written|produced|mixed|mastered|video|artwork|original|cover of|remix of|official audio|official video|lyrics)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ArtistNoiseRegex = new(
        @"\b(?:follow|subscribe|artwork|lyrics|check out|previous|listen|written by|produced by|official audio|official video|original mix|cover|remix of)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static YouTubeDescriptionArtistEvidence Extract(
        string? rawTitle,
        string? description,
        IEnumerable<string?>? additionalTitleAnchors = null)
    {
        if (string.IsNullOrWhiteSpace(rawTitle) || string.IsNullOrWhiteSpace(description))
        {
            return new([], false);
        }

        var anchors = BuildAnchors(rawTitle, additionalTitleAnchors);
        if (anchors.Count == 0)
        {
            return new([], false);
        }

        var lines = description.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var found = new List<YouTubeDescriptionArtistCredit>();
        var excludedSection = false;
        var excludedParagraph = false;

        for (var index = 0; index < lines.Length && index < 1000; index++)
        {
            var line = Clean(lines[index]);
            if (line.Length == 0)
            {
                excludedParagraph = false;
                continue;
            }

            if (ExcludedSectionRegex.IsMatch(line))
            {
                excludedSection = true;
                continue;
            }

            if (ExcludedParagraphRegex.IsMatch(line))
            {
                excludedParagraph = true;
                continue;
            }

            if (excludedSection || excludedParagraph || line.Length > 200
                || line.Contains("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ExcludedLineRegex.IsMatch(line))
            {
                continue;
            }

            // A cover artist heading names the performer of the cover itself. It is
            // useful when the description discusses a source composer separately and
            // does not repeat the recording title next to each credit.
            var coverArtist = CoverArtistHeadingRegex.Match(line);
            if (coverArtist.Success
                && TrackMetadataParser.ExtractTitleSemantics(rawTitle).VersionMarkers.Contains("cover", StringComparer.Ordinal))
            {
                TryAdd(rawTitle, coverArtist.Groups["artist"].Value, line, index);
            }

            var parts = TitleSeparatorRegex.Split(line);
            if (parts.Length == 2)
            {
                TryAdd(parts[1], parts[0], line, index);
                TryAdd(parts[0], parts[1], line, index);
            }

            var byAttribution = ByAttributionRegex.Match(line);
            if (byAttribution.Success)
            {
                TryAdd(byAttribution.Groups["title"].Value, byAttribution.Groups["artist"].Value, line, index);
            }

            var heading = Regex.Replace(line, @"^(?:song|track|title)\s*:\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!MatchesAnchor(heading))
            {
                continue;
            }

            for (var next = index + 1; next <= Math.Min(index + 4, lines.Length - 1); next++)
            {
                var creditLine = Clean(lines[next]);
                if (creditLine.Length == 0)
                {
                    continue;
                }

                var match = HeadingArtistRegex.Match(creditLine);
                if (!match.Success)
                {
                    break;
                }

                TryAdd(heading, match.Groups["artist"].Value, $"{line} / {creditLine}", index);
            }
        }

        var distinct = found
            .DistinctBy(credit => (
                TrackTextNormalizer.Normalize(credit.Title),
                string.Join("|", TrackMetadataParser.Parse(credit.Title, credit.Artist).ArtistCredits)))
            .ToArray();
        var artistFamilies = distinct
            .Select(credit => string.Join("|", TrackMetadataParser.Parse(credit.Title, credit.Artist).ArtistCredits))
            .Where(credits => !string.IsNullOrWhiteSpace(credits))
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new(distinct, artistFamilies > 1);

        bool MatchesAnchor(string title) => anchors.Contains(TrackTextNormalizer.Normalize(Clean(title)));

        void TryAdd(string title, string artist, string evidenceLine, int lineIndex)
        {
            title = Clean(title);
            artist = Clean(artist);
            var parsedTitleAnchor = TrackMetadataParser.Parse(title, null).SearchTitle;
            if ((!MatchesAnchor(title) && !MatchesAnchor(parsedTitleAnchor)) || artist.Length is < 2 or > 100
                || string.IsNullOrWhiteSpace(TrackTextNormalizer.Normalize(artist))
                || artist.Contains(':')
                || artist.Contains('/')
                || ArtistNoiseRegex.IsMatch(artist))
            {
                return;
            }

            var originalSemantics = TrackMetadataParser.ExtractTitleSemantics(rawTitle);
            var proposedSemantics = TrackMetadataParser.ExtractTitleSemantics(title);
            if (proposedSemantics.VersionMarkers.Except(originalSemantics.VersionMarkers).Any()
                || proposedSemantics.PlaybackModifiers.Except(originalSemantics.PlaybackModifiers).Any())
            {
                return;
            }

            var parsedArtist = TrackMetadataParser.Parse(title, artist).ArtistCredits;
            if (parsedArtist.Count == 0)
            {
                return;
            }

            found.Add(new(title, artist, evidenceLine, lineIndex + 1));
        }
    }

    private static HashSet<string> BuildAnchors(string rawTitle, IEnumerable<string?>? additionalTitleAnchors)
    {
        var firstSegment = Regex.Split(rawTitle, @"\s*(?:\|\||\||/{2,})\s*")[0];
        var variants = new List<string>
        {
            firstSegment,
            TrackMetadataParser.Parse(firstSegment, null).SearchTitle,
            TrackMetadataParser.Parse(rawTitle, null).SearchTitle
        };
        variants.AddRange(TitleSeparatorRegex.Split(firstSegment));
        if (additionalTitleAnchors is not null)
        {
            variants.AddRange(additionalTitleAnchors
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!));
        }

        return variants
            .Select(value => TrackTextNormalizer.Normalize(Clean(value ?? string.Empty)))
            .Where(value => value.Length > 1)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Clean(string value) => Regex.Replace(value.Trim(), @"^[^\p{L}\p{Nd}(]+", "")
        .Trim().Trim('"', '\'', '“', '”', '‘', '’');
}
