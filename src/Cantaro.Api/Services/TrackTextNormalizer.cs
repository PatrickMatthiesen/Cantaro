using System.Text.RegularExpressions;

namespace Cantaro.Api.Services;

public static partial class TrackTextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ToLowerInvariant();
        normalized = BracketedNoiseRegex().Replace(normalized, " ");
        normalized = NoiseTokenRegex().Replace(normalized, " ");
        normalized = NonAlphaNumericRegex().Replace(normalized, " ");
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();

        return normalized;
    }

    public static decimal CalculateSimilarity(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);

        if (string.IsNullOrEmpty(normalizedLeft) || string.IsNullOrEmpty(normalizedRight))
        {
            return 0m;
        }

        if (normalizedLeft == normalizedRight)
        {
            return 1m;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
        {
            return 0.9m;
        }

        var leftWords = normalizedLeft.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightWords = normalizedRight.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (leftWords.Length == 0 || rightWords.Length == 0)
        {
            return 0m;
        }

        var leftSet = leftWords.ToHashSet(StringComparer.Ordinal);
        var rightSet = rightWords.ToHashSet(StringComparer.Ordinal);
        var intersection = leftSet.Intersect(rightSet, StringComparer.Ordinal).Count();
        var union = leftSet.Union(rightSet, StringComparer.Ordinal).Count();

        if (union == 0)
        {
            return 0m;
        }

        return Math.Round((decimal)intersection / union, 3, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"\[(.*?)\]|\((.*?)\)", RegexOptions.Compiled)]
    private static partial Regex BracketedNoiseRegex();

    [GeneratedRegex(@"\b(official|video|audio|lyrics|lyric|hd|hq|remaster|remastered)\b", RegexOptions.Compiled)]
    private static partial Regex NoiseTokenRegex();

    [GeneratedRegex(@"[^a-z0-9\s]", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
