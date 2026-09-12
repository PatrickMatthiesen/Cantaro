using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Models;

public sealed class ProfileDto
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public required ProfilePreferencesDto Preferences { get; set; }
}

public sealed class ProfilePreferencesDto
{
    public required string Theme { get; set; }
    public bool NotifyOnSyncSuccess { get; set; }
    public bool NotifyOnSyncFailure { get; set; }
    public bool NotifyOnMediaReview { get; set; }
    public bool KeepPlaylistOrder { get; set; }
    public bool KeepPlaylistMetadata { get; set; }
    public bool HideUnavailableTracks { get; set; }
    public bool ScheduledSync { get; set; }
    public bool BlurEmailAddress { get; set; }
    public required string PreferredMediaReleaseTrack { get; set; }
}

public sealed class UpdateProfileRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public required string DisplayName { get; set; }
}

public sealed class UpdateProfilePreferencesRequest
{
    [Required]
    public required string Theme { get; set; }
    public bool NotifyOnSyncSuccess { get; set; }
    public bool NotifyOnSyncFailure { get; set; }
    public bool NotifyOnMediaReview { get; set; }
    public bool KeepPlaylistOrder { get; set; }
    public bool KeepPlaylistMetadata { get; set; }
    public bool HideUnavailableTracks { get; set; }
    public bool ScheduledSync { get; set; }
    public bool BlurEmailAddress { get; set; }
    public string PreferredMediaReleaseTrack { get; set; } = MediaReleaseTrackPreferences.Default;
}

public sealed class ChangePasswordRequest
{
    [Required]
    public required string CurrentPassword { get; set; }

    [Required, MinLength(6)]
    public required string NewPassword { get; set; }
}

public sealed class DeleteAccountRequest
{
    public string? CurrentPassword { get; set; }
    public bool UseGoogleReauthentication { get; set; }
}
