using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface ITrackSourcePresentationVocabulary
{
    string NormalizePresentationKind(string value);
    string NormalizeUploaderAuthority(string value);
}

public sealed class TrackSourcePresentationVocabulary : ITrackSourcePresentationVocabulary
{
    public string NormalizePresentationKind(string value) => Normalize(
        value,
        TrackSourcePresentationKinds.All,
        nameof(value),
        "provider presentation kind");

    public string NormalizeUploaderAuthority(string value) => Normalize(
        value,
        TrackSourceUploaderAuthorities.All,
        nameof(value),
        "uploader authority");

    private static string Normalize(
        string value,
        IReadOnlyList<string> allowed,
        string parameterName,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        if (!allowed.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Unknown {description}.");
        }

        return normalized;
    }
}
