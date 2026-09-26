using System.Text.Json;
using System.Text.RegularExpressions;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public static class TrackObservationParser
{
    private static readonly string[] ArtistTitleSeparators = [" - ", " \u2013 ", " \u2014 "];

    public static ParsedTrackMetadata Parse(TrackObservation observation)
    {
        var metadata = ReadMetadata(observation);
        return Parse(observation, metadata);
    }

    public static ParsedTrackMetadata Parse(
        TrackObservation observation,
        TrackObservationMetadata? metadata)
    {
        var effectiveTitle = TrackObservationDisplayFormatter.GetQueueTitle(
            observation,
            metadata);
        var effectiveArtist = TrackObservationDisplayFormatter.GetQueueArtist(
            observation,
            metadata);
        var hasStructuredProviderArtist = observation.SourceType.Equals("spotify", StringComparison.OrdinalIgnoreCase)
            || observation.SourceType.Equals("musicbrainz", StringComparison.OrdinalIgnoreCase);
        return TrackMetadataParser.Parse(
            effectiveTitle,
            effectiveArtist,
            parseArtistFromTitle: !hasStructuredProviderArtist);
    }

    public static IReadOnlyList<ParsedTrackMetadata> ParseSearchHypotheses(TrackObservation observation)
    {
        var metadata = ReadMetadata(observation);
        var primary = Parse(observation, metadata);
        var titleHypotheses = new List<ParsedTrackMetadata>();
        if (!string.Equals(observation.SourceType, "youtube", StringComparison.OrdinalIgnoreCase))
        {
            return [primary];
        }

        var originalTitle = metadata?.OriginalTitle ?? observation.Title;
        if (string.IsNullOrWhiteSpace(originalTitle))
        {
            return [primary];
        }

        var originalSemantics = TrackMetadataParser.ExtractTitleSemantics(originalTitle);

        // A video title can put the song first even though the usual dash form puts the artist first.
        // A pipe-delimited cover title followed by a work title is a different form: the suffix is
        // context, not an artist attribution.
        foreach (var separator in TrackMetadataParser.StripEmbeddedWorkContext(originalTitle) is null
            ? ArtistTitleSeparators
            : [])
        {
            var split = originalTitle.IndexOf(separator, StringComparison.Ordinal);
            if (split <= 0)
            {
                continue;
            }

            var title = originalTitle[..split].Trim();
            var artist = originalTitle[(split + separator.Length)..].Split(" | ", 2, StringSplitOptions.TrimEntries)[0];
            AddHypothesis(title, artist);
            break;
        }

        var slashAttribution = Regex.Match(originalTitle, @"^\s*(?<title>.+?)\s*/{3,}\s*(?<artist>.+?)\s*$", RegexOptions.CultureInvariant);
        if (slashAttribution.Success)
        {
            AddHypothesis(slashAttribution.Groups["title"].Value, slashAttribution.Groups["artist"].Value);
        }

        if (originalTitle.StartsWith('【'))
        {
            var closingBracket = originalTitle.IndexOf('】');
            if (closingBracket > 1 && closingBracket < originalTitle.Length - 1)
            {
                var title = originalTitle[(closingBracket + 1)..].Trim();
                var contextStart = title.LastIndexOf('〈');
                if (contextStart > 0 && title.EndsWith('〉'))
                {
                    title = title[..contextStart].Trim();
                }

                AddHypothesis(title, originalTitle[1..closingBracket]);
            }
        }

        var mashupQuery = TrackMatchCreditEvidence.GetNamedMashupCatalogQuery(originalTitle);
        if (mashupQuery is { } catalogQuery && titleHypotheses.Count < 3)
        {
            var parsedQuery = TrackMetadataParser.Parse(
                catalogQuery.Title,
                catalogQuery.Artist,
                parseArtistFromTitle: false);
            if (!string.IsNullOrWhiteSpace(parsedQuery.SearchTitle)
                && !string.IsNullOrWhiteSpace(parsedQuery.SearchArtist))
            {
                titleHypotheses.Add(new ParsedTrackMetadata
                {
                    DisplayTitle = parsedQuery.DisplayTitle,
                    DisplayArtist = parsedQuery.DisplayArtist,
                    SearchTitle = parsedQuery.SearchTitle,
                    SearchArtist = parsedQuery.SearchArtist,
                    ArtistCredits = parsedQuery.ArtistCredits,
                    // This catalog spelling is a search form for the named mashup.
                    // Keep the source semantics so "edit" does not replace "mashup".
                    VersionMarkers = originalSemantics.VersionMarkers,
                    PlaybackModifiers = originalSemantics.PlaybackModifiers,
                    PresentationMarkers = originalSemantics.PresentationMarkers
                });
            }
        }

        var descriptionEvidence = YouTubeDescriptionArtistEvidenceParser.Extract(
            originalTitle,
            metadata?.Description,
            [primary.SearchTitle, metadata?.SearchTitle]);
        var descriptionHypotheses = descriptionEvidence.Credits
            .Select(credit => ParseEvidenceHypothesis(credit.Title, credit.Artist, originalSemantics, credit.EvidenceLine))
            .Where(hypothesis => hypothesis is not null)
            .Cast<ParsedTrackMetadata>()
            .ToList();
        var titleAsArtistFirst = TrackMetadataParser.Parse(originalTitle, null);
        var hasConflictingArtistEvidence = descriptionEvidence.HasConflictingAttributions
            || descriptionHypotheses.Any(descriptionHypothesis =>
                titleAsArtistFirst.ParsedArtistFromTitle
                && string.Equals(descriptionHypothesis.SearchTitle, titleAsArtistFirst.SearchTitle, StringComparison.OrdinalIgnoreCase)
                && !TrackMetadataParser.HaveEquivalentArtistCredits(
                    descriptionHypothesis,
                    titleAsArtistFirst,
                    titleAsArtistFirst.ArtistCredits))
            || descriptionHypotheses.Any(descriptionHypothesis => titleHypotheses.Any(titleHypothesis =>
                string.Equals(descriptionHypothesis.SearchTitle, titleHypothesis.SearchTitle, StringComparison.OrdinalIgnoreCase)
                && !TrackMetadataParser.HaveEquivalentArtistCredits(
                    descriptionHypothesis,
                    titleHypothesis,
                    titleHypothesis.ArtistCredits)));

        // Description credits are direct performer evidence. Keep them ahead of
        // the uploader-derived primary hypothesis and title interpretations.
        var hypotheses = new List<ParsedTrackMetadata>(
            descriptionHypotheses.Count + titleHypotheses.Count + 1);
        hypotheses.AddRange(descriptionHypotheses);
        hypotheses.Add(primary);
        hypotheses.AddRange(titleHypotheses);
        return hypotheses.Select(hypothesis => hasConflictingArtistEvidence
            ? CopyWithConflict(hypothesis)
            : hypothesis).ToArray();

        void AddHypothesis(string title, string artist)
        {
            if (titleHypotheses.Count >= 3 || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            {
                return;
            }

            var parsed = TrackMetadataParser.Parse(title, artist);
            if (string.IsNullOrWhiteSpace(parsed.SearchTitle) || string.IsNullOrWhiteSpace(parsed.SearchArtist)
                || titleHypotheses.Any(existing =>
                    string.Equals(existing.SearchTitle, parsed.SearchTitle, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.SearchArtist, parsed.SearchArtist, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            titleHypotheses.Add(new ParsedTrackMetadata
            {
                DisplayTitle = parsed.DisplayTitle,
                DisplayArtist = parsed.DisplayArtist,
                SearchTitle = parsed.SearchTitle,
                SearchArtist = parsed.SearchArtist,
                ParsedArtistFromTitle = parsed.ParsedArtistFromTitle,
                ArtistCredits = parsed.ArtistCredits,
                VersionMarkers = originalSemantics.VersionMarkers
                    .Concat(parsed.VersionMarkers).Distinct(StringComparer.Ordinal).ToArray(),
                PlaybackModifiers = originalSemantics.PlaybackModifiers
                    .Concat(parsed.PlaybackModifiers).Distinct(StringComparer.Ordinal).ToArray(),
                PresentationMarkers = originalSemantics.PresentationMarkers
                    .Concat(parsed.PresentationMarkers).Distinct(StringComparer.Ordinal).ToArray()
            });
        }
    }

    private static ParsedTrackMetadata? ParseEvidenceHypothesis(
        string title,
        string artist,
        ParsedTitleSemantics sourceSemantics,
        string evidenceLine)
    {
        var parsed = TrackMetadataParser.Parse(title, artist);
        if (string.IsNullOrWhiteSpace(parsed.SearchTitle) || string.IsNullOrWhiteSpace(parsed.SearchArtist))
        {
            return null;
        }

        return new ParsedTrackMetadata
        {
            DisplayTitle = parsed.DisplayTitle,
            DisplayArtist = parsed.DisplayArtist,
            SearchTitle = parsed.SearchTitle,
            SearchArtist = parsed.SearchArtist,
            ParsedArtistFromTitle = parsed.ParsedArtistFromTitle,
            ArtistCredits = parsed.ArtistCredits,
            VersionMarkers = sourceSemantics.VersionMarkers
                .Concat(parsed.VersionMarkers).Distinct(StringComparer.Ordinal).ToArray(),
            PlaybackModifiers = sourceSemantics.PlaybackModifiers
                .Concat(parsed.PlaybackModifiers).Distinct(StringComparer.Ordinal).ToArray(),
            PresentationMarkers = sourceSemantics.PresentationMarkers
                .Concat(parsed.PresentationMarkers).Distinct(StringComparer.Ordinal).ToArray(),
            IsDescriptionArtistEvidence = true,
            ArtistEvidenceLine = evidenceLine
        };
    }

    private static ParsedTrackMetadata CopyWithConflict(ParsedTrackMetadata parsed)
    {
        return new ParsedTrackMetadata
        {
            DisplayTitle = parsed.DisplayTitle,
            DisplayArtist = parsed.DisplayArtist,
            SearchTitle = parsed.SearchTitle,
            SearchArtist = parsed.SearchArtist,
            ParsedArtistFromTitle = parsed.ParsedArtistFromTitle,
            ArtistCredits = parsed.ArtistCredits,
            VersionMarkers = parsed.VersionMarkers,
            PlaybackModifiers = parsed.PlaybackModifiers,
            PresentationMarkers = parsed.PresentationMarkers,
            HasConflictingArtistEvidence = true,
            IsDescriptionArtistEvidence = parsed.IsDescriptionArtistEvidence,
            ArtistEvidenceLine = parsed.ArtistEvidenceLine
        };
    }

    public static TrackObservationMetadata? ReadMetadata(TrackObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.RawMetadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TrackObservationMetadata>(
                observation.RawMetadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
