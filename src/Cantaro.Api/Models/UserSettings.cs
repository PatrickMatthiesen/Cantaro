namespace Cantaro.Api.Models;

public static class UserThemePreferences
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        System,
        Light,
        Dark
    };
}

public static class MediaReleaseTrackPreferences
{
    public const string Default = "sub:en";

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var presentation = parts[0].ToLowerInvariant();
        if (presentation is not "sub" and not "dub" || !TryNormalizeLanguageCode(parts[1], out var languageCode))
        {
            return false;
        }

        normalized = $"{presentation}:{languageCode}";
        return true;
    }

    private static bool TryNormalizeLanguageCode(string value, out string normalized)
    {
        normalized = string.Empty;
        var subtags = value.Split('-', StringSplitOptions.TrimEntries);
        if (subtags.Length == 0
            || subtags.Any(subtag => subtag.Length is < 2 or > 8 || !subtag.All(char.IsLetterOrDigit))
            || subtags[0].Length is < 2 or > 3
            || !subtags[0].All(char.IsLetter))
        {
            return false;
        }

        normalized = string.Join('-', subtags.Select((subtag, index) =>
            index == 0
                ? subtag.ToLowerInvariant()
                : subtag.Length == 2 && subtag.All(char.IsLetter)
                    ? subtag.ToUpperInvariant()
                    : subtag.ToLowerInvariant()));
        return true;
    }
}

public class UserSettings
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string? DisplayName { get; set; }
    public string Theme { get; set; } = UserThemePreferences.System;
    public bool NotifyOnSyncSuccess { get; set; } = true;
    public bool NotifyOnSyncFailure { get; set; } = true;
    public bool NotifyOnMediaReview { get; set; } = true;
    public bool KeepPlaylistOrder { get; set; } = true;
    public bool KeepPlaylistMetadata { get; set; } = true;
    public bool HideUnavailableTracks { get; set; } = true;
    public bool ScheduledSync { get; set; } = true;
    public bool BlurEmailAddress { get; set; }
    public string PreferredMediaReleaseTrack { get; set; } = MediaReleaseTrackPreferences.Default;

    public string? AvatarObjectKey { get; set; }
    public string? AvatarETag { get; set; }
    public Guid? AvatarVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
