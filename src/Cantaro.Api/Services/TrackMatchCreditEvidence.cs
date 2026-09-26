using System.Text.RegularExpressions;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

internal static partial class TrackMatchCreditEvidence
{
    public static (string Title, string Artist)? GetNamedMashupCatalogQuery(string title)
    {
        var mashup = NamedMashupRegex().Match(title);
        return mashup.Success
            ? ($"{mashup.Groups["first"].Value} - {mashup.Groups["second"].Value} Edit", mashup.Groups["creator"].Value)
            : null;
    }

    public static bool HasCompatibleFeaturedCredits(
        TrackObservation source, ParsedTrackMetadata parsedSource,
        TrackMatchSearchCandidate candidate, bool exactTitle)
    {
        // Catalogs sometimes omit the video's featured collaborator. Never infer
        // that a missing primary performer is merely a featured credit.
        if (!exactTitle || string.IsNullOrWhiteSpace(candidate.Isrc)) return false;
        var title = TrackObservationParser.ReadMetadata(source)?.OriginalTitle ?? source.Title;
        var featured = TrackMetadataParser.NormalizeArtistCredits(
            TrackMetadataParser.ExtractFeaturedArtistNames(title));
        if (featured.Count == 0) return false;
        var primary = parsedSource.ArtistCredits.Except(featured, StringComparer.Ordinal).ToArray();
        var catalog = TrackMetadataParser.NormalizeArtistCredits(candidate.ArtistCredits);
        return primary.Length > 0 && catalog.Count > 0
            && primary.All(credit => catalog.Contains(credit, StringComparer.Ordinal))
            && catalog.All(credit => parsedSource.ArtistCredits.Contains(credit, StringComparer.Ordinal));
    }

    public static bool IsSameNamedMashup(
        TrackObservation source, TrackMatchSearchCandidate candidate,
        ParsedTrackMetadata parsedSource, ParsedTrackMetadata parsedCandidate)
    {
        // A creator's "A x B (Creator Mashup)" can be cataloged as "A - B Edit".
        // Require both complete work names and the named creator, not just one
        // overlapping song title or a generic equivalence between edit/remix labels.
        var title = TrackObservationParser.ReadMetadata(source)?.OriginalTitle ?? source.Title;
        var mashup = NamedMashupRegex().Match(title);
        var edit = CatalogMashupEditRegex().Match(candidate.Title);
        if (!mashup.Success || !edit.Success || string.IsNullOrWhiteSpace(candidate.Isrc)) return false;
        var creator = TrackMetadataParser.NormalizeArtistCredits([mashup.Groups["creator"].Value]);
        var credits = TrackMetadataParser.NormalizeArtistCredits(candidate.ArtistCredits);
        return creator.Count > 0 && creator.SequenceEqual(credits, StringComparer.Ordinal)
            && creator.All(credit => parsedSource.ArtistCredits.Contains(credit, StringComparer.Ordinal))
            && TrackTextNormalizer.AreEquivalentTitles(mashup.Groups["first"].Value, edit.Groups["first"].Value)
            && TrackTextNormalizer.AreEquivalentTitles(mashup.Groups["second"].Value, edit.Groups["second"].Value)
            && TrackMatchScorer.HaveSameMarkers(parsedSource.PlaybackModifiers, parsedCandidate.PlaybackModifiers)
            && TrackMatchScorer.HaveSameMarkers(
                parsedSource.VersionMarkers.Where(marker => marker != "mashup").ToArray(),
                parsedCandidate.VersionMarkers.Where(marker => marker != "edit").ToArray());
    }

    [GeneratedRegex(@"^(?<first>.+?)\s+[x×]\s+(?<second>.+?)\s*\((?<creator>[^()]+?)\s+mashup\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedMashupRegex();

    [GeneratedRegex(@"^(?<first>.+?)\s+[-–—]\s+(?<second>.+?)\s+edit\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatalogMashupEditRegex();
}
