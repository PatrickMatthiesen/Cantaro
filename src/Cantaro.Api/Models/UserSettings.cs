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

    public string? AvatarObjectKey { get; set; }
    public string? AvatarETag { get; set; }
    public Guid? AvatarVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
