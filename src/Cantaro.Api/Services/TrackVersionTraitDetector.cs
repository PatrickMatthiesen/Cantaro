using System.Text.RegularExpressions;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public sealed record TrackVersionTraitProposal(
    string TraitKey,
    decimal Confidence,
    string EvidenceMethod,
    string MethodVersion,
    IReadOnlyList<string> EvidenceMarkers);

public interface ITrackVersionTraitDetector
{
    IReadOnlyList<TrackVersionTraitProposal> Detect(string? rawTitle);
}

/// <summary>
/// Produces conservative proposals from contextual title annotations. It does
/// not share marker extraction with matching, persist evidence, or group Tracks.
/// </summary>
public sealed partial class TitleMarkerTrackVersionTraitDetector : ITrackVersionTraitDetector
{
    public const string EvidenceMethod = "contextual-title-marker";
    public const string MethodVersion = "1";

    private static readonly IReadOnlyDictionary<string, (string TraitKey, decimal Confidence)> MarkerMappings =
        new Dictionary<string, (string, decimal)>(StringComparer.Ordinal)
        {
            ["original"] = (TrackVersionTraitKeys.Original, 0.75m),
            ["acoustic"] = (TrackVersionTraitKeys.Acoustic, 0.90m),
            ["orchestral"] = (TrackVersionTraitKeys.Orchestral, 0.90m),
            ["live"] = (TrackVersionTraitKeys.Live, 0.85m),
            ["instrumental"] = (TrackVersionTraitKeys.Instrumental, 0.90m),
            ["a-cappella"] = (TrackVersionTraitKeys.ACappella, 0.90m),
            ["remix"] = (TrackVersionTraitKeys.Remix, 0.90m),
            ["vip-mix"] = (TrackVersionTraitKeys.Remix, 0.85m),
            ["club-mix"] = (TrackVersionTraitKeys.Remix, 0.85m),
            ["cover"] = (TrackVersionTraitKeys.Cover, 0.80m),
            ["edit"] = (TrackVersionTraitKeys.Edit, 0.85m),
            ["radio-edit"] = (TrackVersionTraitKeys.Edit, 0.90m),
            ["extended-mix"] = (TrackVersionTraitKeys.Edit, 0.80m),
            ["clean"] = (TrackVersionTraitKeys.Edit, 0.75m),
            ["dirty"] = (TrackVersionTraitKeys.Edit, 0.75m),
            ["intro"] = (TrackVersionTraitKeys.Edit, 0.70m),
            ["outro"] = (TrackVersionTraitKeys.Edit, 0.70m),
            ["demo"] = (TrackVersionTraitKeys.Demo, 0.90m),
            ["remaster"] = (TrackVersionTraitKeys.Remaster, 0.90m),
            ["karaoke"] = (TrackVersionTraitKeys.Karaoke, 0.85m),
            ["stripped"] = (TrackVersionTraitKeys.Stripped, 0.85m)
        };

    public IReadOnlyList<TrackVersionTraitProposal> Detect(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return [];
        }

        var contexts = BracketedContextRegex().Matches(rawTitle)
            .Select(match => new DetectionContext(match.Groups["text"].Value, true))
            .ToList();
        var suffix = SuffixContextRegex().Match(rawTitle);
        if (suffix.Success)
        {
            contexts.Add(new DetectionContext(suffix.Groups["text"].Value, false));
        }

        var evidence = new Dictionary<string, List<(string Marker, decimal Confidence)>>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            DetectContext(context, evidence);
        }

        return evidence
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TrackVersionTraitProposal(
                pair.Key,
                pair.Value.Max(item => item.Confidence),
                EvidenceMethod,
                MethodVersion,
                pair.Value.Select(item => item.Marker)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(marker => marker, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static void DetectContext(
        DetectionContext context,
        IDictionary<string, List<(string Marker, decimal Confidence)>> evidence)
    {
        var versionMatches = VersionMarkerRegex().Matches(context.Text).ToArray();
        if (versionMatches.Length == 0)
        {
            return;
        }

        var presentationMatches = PresentationMarkerRegex().Matches(context.Text).ToArray();
        if (!IsConstrainedContext(context, versionMatches, presentationMatches))
        {
            return;
        }

        foreach (var match in versionMatches)
        {
            if (presentationMatches.Any(presentation => Contains(presentation, match)))
            {
                continue;
            }

            var marker = NormalizeVersionMarker(match.Value);
            if (!MarkerMappings.TryGetValue(marker, out var mapping))
            {
                continue;
            }

            if (!evidence.TryGetValue(mapping.TraitKey, out var markers))
            {
                markers = [];
                evidence.Add(mapping.TraitKey, markers);
            }

            markers.Add((marker, mapping.Confidence));
        }
    }

    private static bool IsConstrainedContext(
        DetectionContext context,
        IReadOnlyCollection<Match> versionMatches,
        IReadOnlyCollection<Match> presentationMatches)
    {
        var remainder = context.Text.ToCharArray();
        foreach (var match in versionMatches.Concat(presentationMatches))
        {
            Array.Fill(remainder, ' ', match.Index, match.Length);
        }

        var unmatched = SuffixFillerRegex().Replace(new string(remainder), string.Empty);
        return string.IsNullOrWhiteSpace(unmatched.Trim(' ', '-', '|', '/', '&', '+', ',', '[', ']', '(', ')'))
            || (context.IsBracketed && LiveVenueContextRegex().IsMatch(context.Text.Trim()));
    }

    private static bool Contains(Match outer, Match inner) =>
        inner.Index >= outer.Index && inner.Index + inner.Length <= outer.Index + outer.Length;

    private static string NormalizeVersionMarker(string value)
    {
        var marker = WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
        if (marker.StartsWith("original", StringComparison.Ordinal)) return "original";
        if (marker.Contains("remaster", StringComparison.Ordinal)) return "remaster";
        if (marker.StartsWith("radio", StringComparison.Ordinal)) return "radio-edit";
        if (marker.StartsWith("vip", StringComparison.Ordinal)) return "vip-mix";
        if (marker.StartsWith("extended", StringComparison.Ordinal)) return "extended-mix";
        if (marker.StartsWith("club", StringComparison.Ordinal)) return "club-mix";
        if (marker.Contains("remix", StringComparison.Ordinal)) return "remix";
        if (ACappellaRegex().IsMatch(marker)) return "a-cappella";
        return marker;
    }

    private sealed record DetectionContext(string Text, bool IsBracketed);

    [GeneratedRegex(@"[\[(](?<text>.*?)[\])]")]
    private static partial Regex BracketedContextRegex();

    [GeneratedRegex(@"^.*(?:\s(?:-|[|–—])\s)(?<text>.+)$")]
    private static partial Regex SuffixContextRegex();

    [GeneratedRegex(@"\b(original(?:\s+(?:mix|version))?|acoustic|orchestral|live|remix(?:ed)?|remaster(?:ed)?|instrumental|karaoke|demo|dirty|clean|intro|outro|radio(?:\s+(?:edit|version))?|vip\s+mix|extended(?:\s+mix)?|club\s+mix|a[\s-]?cap{1,2}ella|stripped|cover|edit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionMarkerRegex();

    [GeneratedRegex(@"\b(official[\s\-–—]+(?:music[\s\-–—]+)?video|lyric(?:s)?[\s\-–—]+video|live[\s\-–—]+video|cover[\s\-–—]+art[\s\-–—]+audio|visualizer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PresentationMarkerRegex();

    [GeneratedRegex(@"\b(version|mix|recording|performance)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixFillerRegex();

    [GeneratedRegex(@"^live\s+(?:at|from)\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex LiveVenueContextRegex();

    [GeneratedRegex(@"^a[\s-]?cap{1,2}ella$", RegexOptions.IgnoreCase)]
    private static partial Regex ACappellaRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
