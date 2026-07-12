using System.Text.RegularExpressions;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public enum TrackSourceVerifiedUploaderAuthority
{
    Official,
    User
}

public sealed record TrackSourceUploaderAuthorityEvidence(
    TrackSourceVerifiedUploaderAuthority Authority,
    string EvidenceIdentity);

public sealed record TrackSourcePresentationDetectionInput(
    string SourceType,
    string ExternalId,
    string? Title,
    string MetadataEvidenceIdentity,
    TrackSourceUploaderAuthorityEvidence? UploaderAuthorityEvidence = null);

public sealed record TrackSourcePresentationDetectionResult(
    TrackSourceClassificationProposal? PresentationKind,
    TrackSourceClassificationProposal? UploaderAuthority,
    IReadOnlyList<string> ConflictingPresentationKinds);

public interface ITrackSourcePresentationDetector
{
    TrackSourcePresentationDetectionResult Detect(TrackSourcePresentationDetectionInput input);
}

/// <summary>
/// Produces provider-presentation proposals without persisting them or changing
/// musical-version evidence. Uploader authority is proposed only from an
/// affirmative provider signal, never from title text or a missing signal.
/// </summary>
public sealed partial class TrackSourcePresentationDetector : ITrackSourcePresentationDetector
{
    public const string ProviderMediaTypeEvidenceMethod = "provider-media-type";
    public const string TitleMarkerEvidenceMethod = "contextual-presentation-marker";
    public const string UploaderSignalEvidenceMethod = "provider-uploader-signal";
    public const string MethodVersion = "1";

    public TrackSourcePresentationDetectionResult Detect(TrackSourcePresentationDetectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sourceType = Required(input.SourceType, nameof(input.SourceType), 64).ToLowerInvariant();
        _ = Required(input.ExternalId, nameof(input.ExternalId), 256);
        var metadataEvidenceIdentity = Required(
            input.MetadataEvidenceIdentity,
            nameof(input.MetadataEvidenceIdentity),
            256);

        return sourceType switch
        {
            "spotify" => new TrackSourcePresentationDetectionResult(
                Proposal(
                    TrackSourcePresentationKinds.Audio,
                    1m,
                    sourceType,
                    metadataEvidenceIdentity,
                    ProviderMediaTypeEvidenceMethod),
                null,
                []),
            "youtube" => DetectYouTube(input, sourceType, metadataEvidenceIdentity),
            _ => new TrackSourcePresentationDetectionResult(null, null, [])
        };
    }

    private static TrackSourcePresentationDetectionResult DetectYouTube(
        TrackSourcePresentationDetectionInput input,
        string sourceType,
        string metadataEvidenceIdentity)
    {
        var kinds = DetectYouTubeKinds(input.Title);
        var conflicts = kinds.Count > 1 ? kinds : [];
        var kind = kinds.Count == 1
            ? Proposal(
                kinds[0],
                KindConfidence(kinds[0]),
                sourceType,
                metadataEvidenceIdentity,
                TitleMarkerEvidenceMethod)
            : null;

        return new TrackSourcePresentationDetectionResult(
            kind,
            DetectYouTubeAuthority(input.UploaderAuthorityEvidence, sourceType),
            conflicts);
    }

    private static IReadOnlyList<string> DetectYouTubeKinds(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var contexts = BracketedContextRegex().Matches(title)
            .Select(match => match.Groups["text"].Value)
            .ToList();
        var suffix = SuffixContextRegex().Match(title);
        if (suffix.Success)
        {
            contexts.Add(suffix.Groups["text"].Value);
        }

        return contexts
            .SelectMany(DetectContext)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> DetectContext(string context)
    {
        var matches = PresentationMarkerRegex().Matches(context).ToArray();
        if (matches.Length == 0)
        {
            return [];
        }

        var remainder = context.ToCharArray();
        foreach (var match in matches)
        {
            Array.Fill(remainder, ' ', match.Index, match.Length);
        }

        if (!string.IsNullOrWhiteSpace(new string(remainder).Trim(' ', '-', '|', '/', '&', '+', ',', '[', ']', '(', ')')))
        {
            return [];
        }

        return matches.Select(KindFor);
    }

    private static string KindFor(Match match) => match.Groups["lyric"].Success
        ? TrackSourcePresentationKinds.LyricVideo
        : match.Groups["coverArt"].Success
            ? TrackSourcePresentationKinds.CoverArtAudio
            : match.Groups["live"].Success
                ? TrackSourcePresentationKinds.LiveVideo
                : match.Groups["music"].Success
                    ? TrackSourcePresentationKinds.MusicVideo
                    : match.Groups["visualizer"].Success
                        ? TrackSourcePresentationKinds.Visualizer
                        : TrackSourcePresentationKinds.Audio;

    private static decimal KindConfidence(string kind) => kind is
        TrackSourcePresentationKinds.LyricVideo or TrackSourcePresentationKinds.CoverArtAudio
            ? 0.95m
            : 0.90m;

    private static TrackSourceClassificationProposal? DetectYouTubeAuthority(
        TrackSourceUploaderAuthorityEvidence? evidence,
        string sourceType) => evidence?.Authority switch
        {
            null => null,
            TrackSourceVerifiedUploaderAuthority.Official => Proposal(
                TrackSourceUploaderAuthorities.Official,
                1m,
                sourceType,
                Required(evidence.EvidenceIdentity, nameof(evidence.EvidenceIdentity), 256),
                UploaderSignalEvidenceMethod),
            TrackSourceVerifiedUploaderAuthority.User => Proposal(
                TrackSourceUploaderAuthorities.User,
                1m,
                sourceType,
                Required(evidence.EvidenceIdentity, nameof(evidence.EvidenceIdentity), 256),
                UploaderSignalEvidenceMethod),
            _ => throw new ArgumentOutOfRangeException(
                nameof(evidence),
                evidence.Authority,
                "Unknown verified uploader authority.")
        };

    private static TrackSourceClassificationProposal Proposal(
        string value,
        decimal confidence,
        string evidenceSource,
        string evidenceIdentity,
        string evidenceMethod) => new(
            value,
            confidence,
            evidenceSource,
            evidenceIdentity,
            evidenceMethod,
            MethodVersion);

    private static string Required(string value, string name, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    [GeneratedRegex(@"[\[(](?<text>.*?)[\])]")]
    private static partial Regex BracketedContextRegex();

    [GeneratedRegex(@"^.*(?:\s(?:-|[|–—])\s)(?<text>.+)$")]
    private static partial Regex SuffixContextRegex();

    [GeneratedRegex(
        @"\b(?:(?<lyric>(?:official[\s\-–—]+)?lyric(?:s)?[\s\-–—]+video)|(?<coverArt>cover[\s\-–—]+art[\s\-–—]+audio)|(?<live>(?:official[\s\-–—]+)?live[\s\-–—]+video)|(?<music>(?:official[\s\-–—]+(?:music[\s\-–—]+)?video|music[\s\-–—]+video))|(?<visualizer>(?:(?:official|audio)[\s\-–—]+)?visualizer)|(?<audio>official[\s\-–—]+audio))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex PresentationMarkerRegex();
}
