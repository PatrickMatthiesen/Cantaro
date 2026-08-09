using System.IO.Compression;
using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(
    ApplicationDbContext dbContext,
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IAvatarStore avatarStore,
    ILogger<ProfileController> logger) : ControllerBase
{
    private const long MaxAvatarBytes = 1024 * 1024;
    private const string AvatarContentType = "image/webp";
    private const string JpegContentType = "image/jpeg";
    private const string PngContentType = "image/png";

    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var settings = await GetOrCreateSettingsAsync(user, cancellationToken);
        return Ok(MapProfile(user, settings));
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var displayName = request.DisplayName.Trim();
        if (displayName.Length is < 1 or > 100)
        {
            return BadRequest(new { error = "Display name must be between 1 and 100 characters." });
        }

        var settings = await GetOrCreateSettingsAsync(user, cancellationToken);
        settings.DisplayName = displayName;
        settings.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapProfile(user, settings));
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<ProfileDto>> UpdatePreferences(UpdateProfilePreferencesRequest request, CancellationToken cancellationToken)
    {
        if (!UserThemePreferences.All.Contains(request.Theme))
        {
            return BadRequest(new { error = "Theme must be system, light, or dark." });
        }

        if (!MediaReleaseTrackPreferences.TryNormalize(request.PreferredMediaReleaseTrack, out var preferredMediaReleaseTrack))
        {
            return BadRequest(new { error = "Preferred media release track must use sub or dub with a language code, for example sub:en or dub:en." });
        }

        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var settings = await GetOrCreateSettingsAsync(user, cancellationToken);
        settings.Theme = request.Theme;
        settings.NotifyOnSyncSuccess = request.NotifyOnSyncSuccess;
        settings.NotifyOnSyncFailure = request.NotifyOnSyncFailure;
        settings.NotifyOnMediaReview = request.NotifyOnMediaReview;
        settings.KeepPlaylistOrder = request.KeepPlaylistOrder;
        settings.KeepPlaylistMetadata = request.KeepPlaylistMetadata;
        settings.HideUnavailableTracks = request.HideUnavailableTracks;
        settings.ScheduledSync = request.ScheduledSync;
        settings.BlurEmailAddress = request.BlurEmailAddress;
        settings.PreferredMediaReleaseTrack = preferredMediaReleaseTrack;
        settings.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapProfile(user, settings));
    }

    [RequestSizeLimit(MaxAvatarBytes + 16_384)]
    [HttpPost("avatar")]
    public async Task<ActionResult<ProfileDto>> UploadAvatar([FromForm] IFormFile avatar, CancellationToken cancellationToken)
    {
        if (avatar.Length is <= 0 or > MaxAvatarBytes)
        {
            return BadRequest(new { error = "Avatar must be an image no larger than 1 MiB." });
        }

        await using var content = new MemoryStream((int)avatar.Length);
        await avatar.CopyToAsync(content, cancellationToken);
        var contentType = ResolveAvatarContentType(content.GetBuffer().AsSpan(0, (int)content.Length));
        if (contentType is null || !string.Equals(avatar.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Avatar must be a valid WebP, JPEG, or PNG image." });
        }

        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var settings = await GetOrCreateSettingsAsync(user, cancellationToken);
        var previousObjectKey = settings.AvatarObjectKey;
        var version = Guid.NewGuid();
        var objectKey = $"avatars/{user.Id}/{version:N}.{AvatarFileExtension(contentType)}";
        content.Position = 0;

        string etag;
        try
        {
            etag = await avatarStore.PutAsync(objectKey, content, content.Length, contentType, cancellationToken);
            settings.AvatarObjectKey = objectKey;
            settings.AvatarETag = etag;
            settings.AvatarVersion = version;
            settings.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteAvatarAsync(objectKey, cancellationToken);
            throw;
        }

        if (previousObjectKey is not null)
        {
            await TryDeleteAvatarAsync(previousObjectKey, cancellationToken);
        }
        return Ok(MapProfile(user, settings));
    }

    [HttpGet("avatar")]
    public async Task<ActionResult> GetAvatar([FromQuery] string? v, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var settings = await dbContext.UserSettings.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (settings?.AvatarObjectKey is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(v)
            && !string.Equals(v, settings.AvatarVersion?.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var avatar = await avatarStore.GetAsync(settings.AvatarObjectKey, cancellationToken);
        if (avatar is null) return NotFound();

        var etag = string.IsNullOrWhiteSpace(settings.AvatarETag) ? avatar.ETag : settings.AvatarETag;
        var quotedEtag = $"\"{etag.Trim('\"')}\"";
        Response.Headers.ETag = quotedEtag;
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (Request.Headers.IfNoneMatch.Any(value => string.Equals(value, quotedEtag, StringComparison.Ordinal)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(avatar.Content, ResolveAvatarContentType(avatar.Content) ?? AvatarContentType);
    }

    [HttpDelete("avatar")]
    public async Task<ActionResult<ProfileDto>> DeleteAvatar(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var settings = await GetOrCreateSettingsAsync(user, cancellationToken);
        var objectKey = settings.AvatarObjectKey;
        settings.AvatarObjectKey = null;
        settings.AvatarETag = null;
        settings.AvatarVersion = null;
        settings.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (objectKey is not null) await TryDeleteAvatarAsync(objectKey, cancellationToken);
        return Ok(MapProfile(user, settings));
    }

    [HttpPost("change-password")]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = string.Join(" ", result.Errors.Select(error => error.Description)) });
        }
        await signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<ActionResult> Export(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null) return Unauthorized();
        var settings = await GetOrCreateSettingsAsync(user, cancellationToken);

        var connectedAccounts = await dbContext.ConnectedServiceAccounts.AsNoTracking()
            .Where(account => account.UserId == user.Id)
            .Select(account => new { account.Service, account.DisplayName, account.ExternalAccountId, account.CreatedAt, account.UpdatedAt })
            .ToListAsync(cancellationToken);
        var playlists = await dbContext.Playlists.AsNoTracking()
            .Where(playlist => playlist.UserId == user.Id)
            .Include(playlist => playlist.Entries)
            .Include(playlist => playlist.ServiceMappings)
            .ToListAsync(cancellationToken);
        var mediaLibrary = await dbContext.MediaLibraryEntries.AsNoTracking()
            .Where(entry => entry.UserId == user.Id)
            .Select(entry => new
            {
                entry.Id, entry.MediaTitleId, entry.Provider, entry.ProviderAccountId, entry.ProviderMediaId,
                entry.Status, entry.ProgressEpisodes, entry.ProgressChapters, entry.ProgressVolumes,
                entry.LastSyncedAt, entry.LastRemoteUpdateAt, entry.LastLocalEditAt, entry.CreatedAt, entry.UpdatedAt
            }).ToListAsync(cancellationToken);
        var observations = await dbContext.MediaObservations.AsNoTracking()
            .Where(observation => observation.UserId == user.Id)
            .Select(observation => new
            {
                observation.Id, observation.SiteIdentifier, observation.SiteMediaId, observation.ObservedTitle,
                observation.ProgressHint, observation.ObservedAt, observation.MatchStatus,
                observation.MediaTitleId, observation.CreatedAt, observation.UpdatedAt
            }).ToListAsync(cancellationToken);

        var export = new
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Profile = new { user.Id, user.Email, DisplayName = ResolveDisplayName(user, settings), user.CreatedAt },
            Preferences = MapPreferences(settings),
            ConnectedAccounts = connectedAccounts,
            Playlists = playlists.Select(playlist => new
            {
                playlist.Id, playlist.Name, playlist.Description, playlist.CreatedAt, playlist.UpdatedAt,
                Entries = playlist.Entries.Select(entry => new { entry.Id, entry.TrackId, entry.Position, entry.AddedAt, entry.SourceService }),
                ServiceMappings = playlist.ServiceMappings.Select(mapping => new { mapping.Service, mapping.ServicePlaylistId, mapping.SyncMode, mapping.LastSyncedAt, mapping.LastSyncStatus })
            }),
            MediaLibrary = mediaLibrary,
            MediaObservations = observations
        };

        await using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var dataEntry = archive.CreateEntry("cantaro-export.json", CompressionLevel.SmallestSize);
            await using (var stream = dataEntry.Open())
            {
                await JsonSerializer.SerializeAsync(stream, export, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            }
            if (settings.AvatarObjectKey is not null && await avatarStore.GetAsync(settings.AvatarObjectKey, cancellationToken) is { } avatar)
            {
                var avatarEntry = archive.CreateEntry($"avatar.{AvatarFileExtension(avatar.ContentType)}", CompressionLevel.NoCompression);
                await using var stream = avatarEntry.Open();
                await stream.WriteAsync(avatar.Content, cancellationToken);
            }
        }
        return File(archiveStream.ToArray(), "application/zip", $"cantaro-export-{DateTime.UtcNow:yyyy-MM-dd}.zip");
    }

    [HttpDelete]
    public async Task<ActionResult> DeleteAccount(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
        {
            return BadRequest(new { error = "The current password is incorrect." });
        }

        var avatarObjectKey = await dbContext.UserSettings.AsNoTracking()
            .Where(settings => settings.UserId == user.Id)
            .Select(settings => settings.AvatarObjectKey)
            .SingleOrDefaultAsync(cancellationToken);
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { error = string.Join(" ", result.Errors.Select(error => error.Description)) });
        }
        await signInManager.SignOutAsync();
        if (avatarObjectKey is not null) await TryDeleteAvatarAsync(avatarObjectKey, cancellationToken);
        return NoContent();
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        return int.TryParse(userId, out var id)
            ? await dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken)
            : null;
    }

    private async Task<UserSettings> GetOrCreateSettingsAsync(User user, CancellationToken cancellationToken)
    {
        var settings = await dbContext.UserSettings.SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (settings is not null) return settings;
        settings = new UserSettings { UserId = user.Id, DisplayName = DeriveDisplayName(user.Email), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        dbContext.UserSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static ProfileDto MapProfile(User user, UserSettings settings) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        DisplayName = ResolveDisplayName(user, settings),
        AvatarUrl = settings.AvatarVersion is { } version ? $"/api/profile/avatar?v={version:N}" : null,
        CreatedAt = user.CreatedAt,
        Preferences = MapPreferences(settings)
    };

    private static ProfilePreferencesDto MapPreferences(UserSettings settings) => new()
    {
        Theme = settings.Theme,
        NotifyOnSyncSuccess = settings.NotifyOnSyncSuccess,
        NotifyOnSyncFailure = settings.NotifyOnSyncFailure,
        NotifyOnMediaReview = settings.NotifyOnMediaReview,
        KeepPlaylistOrder = settings.KeepPlaylistOrder,
        KeepPlaylistMetadata = settings.KeepPlaylistMetadata,
        HideUnavailableTracks = settings.HideUnavailableTracks,
        ScheduledSync = settings.ScheduledSync,
        BlurEmailAddress = settings.BlurEmailAddress,
        PreferredMediaReleaseTrack = settings.PreferredMediaReleaseTrack
    };

    private static string ResolveDisplayName(User user, UserSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DisplayName) ? DeriveDisplayName(user.Email) : settings.DisplayName;

    private static string DeriveDisplayName(string? email)
    {
        var localPart = email?.Split('@', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(localPart) ? "Cantaro listener" : localPart;
    }

    private static string? ResolveAvatarContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return AvatarContentType;
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return JpegContentType;
        }

        return bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A
                ? PngContentType
                : null;
    }

    private static string AvatarFileExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        JpegContentType => "jpg",
        PngContentType => "png",
        _ => "webp"
    };

    private async Task TryDeleteAvatarAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await avatarStore.DeleteAsync(objectKey, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to delete avatar object {ObjectKey}; it may require later cleanup.", objectKey);
        }
    }
}
