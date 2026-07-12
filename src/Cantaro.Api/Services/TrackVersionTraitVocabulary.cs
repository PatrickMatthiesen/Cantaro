using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public interface ITrackVersionTraitVocabulary
{
    IReadOnlyList<string> TraitKeys { get; }
    string Normalize(string value);
    bool ArePotentiallyConflicting(string left, string right);
}

/// <summary>
/// Application-owned controlled vocabulary. Persisted keys remain strings so
/// vocabulary changes do not turn one combinable trait into an exclusive enum.
/// </summary>
public sealed class TrackVersionTraitVocabulary : ITrackVersionTraitVocabulary
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acapella"] = TrackVersionTraitKeys.ACappella,
            ["a-cappella"] = TrackVersionTraitKeys.ACappella
        };

    private static readonly IReadOnlySet<(string Left, string Right)> PotentialConflicts =
        new HashSet<(string, string)>
        {
            Ordered(TrackVersionTraitKeys.Original, TrackVersionTraitKeys.Cover),
            Ordered(TrackVersionTraitKeys.Original, TrackVersionTraitKeys.Remix),
            Ordered(TrackVersionTraitKeys.Original, TrackVersionTraitKeys.Demo)
        };

    public IReadOnlyList<string> TraitKeys => TrackVersionTraitKeys.All;

    public string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = Aliases.GetValueOrDefault(normalized, normalized);
        if (!TrackVersionTraitKeys.IsKnown(normalized))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Unknown musical-version trait.");
        }

        return normalized;
    }

    public bool ArePotentiallyConflicting(string left, string right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return normalizedLeft != normalizedRight
            && PotentialConflicts.Contains(Ordered(normalizedLeft, normalizedRight));
    }

    private static (string Left, string Right) Ordered(string left, string right) =>
        string.CompareOrdinal(left, right) <= 0 ? (left, right) : (right, left);
}
