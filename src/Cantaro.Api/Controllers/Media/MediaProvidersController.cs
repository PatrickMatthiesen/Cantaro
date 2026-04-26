using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

internal sealed class MediaProviderOAuthState
{
    public required int UserId { get; set; }
    public required string ProviderId { get; set; }
    public required string ReturnUrl { get; set; }
    public required string Trigger { get; set; }
    public required string CodeVerifier { get; set; }
    public required long CreatedAtTicksUtc { get; set; }
}

[ApiController]
[Route("api/media")]
[Authorize]
public class MediaProvidersController(
    ApplicationDbContext dbContext,
    IMediaProviderRegistry mediaProviderRegistry,
    MediaLibraryImportService mediaLibraryImportService,
    MediaProviderOperationProcessor mediaProviderOperationProcessor,
    UserManager<User> userManager,
    ILogger<MediaProvidersController> logger,
    IDataProtectionProvider dataProtectionProvider,
    IFrontendUrlResolver urlResolver) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly MediaLibraryImportService _mediaLibraryImportService = mediaLibraryImportService;
    private readonly MediaProviderOperationProcessor _mediaProviderOperationProcessor = mediaProviderOperationProcessor;
    private readonly UserManager<User> _userManager = userManager;
    private readonly ILogger<MediaProvidersController> _logger = logger;
    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("MediaProvider.OAuth.State");
    private readonly IFrontendUrlResolver _urlResolver = urlResolver;

    [HttpGet("providers/{providerId}/status")]
    public async Task<ActionResult<MediaProviderAccountStatusDto>> GetStatus(string providerId, CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var account = await provider.GetConnectedAccountAsync(userId, cancellationToken);

        return Ok(new MediaProviderAccountStatusDto
        {
            ProviderId = provider.ProviderId,
            IsConnected = account is not null,
            DisplayName = account?.DisplayName,
            ExternalAccountId = account?.ExternalAccountId,
            ConnectedAt = account?.CreatedAt
        });
    }

    [HttpGet("providers/{providerId}/connect")]
    public async Task<ActionResult> Connect(string providerId, [FromQuery] string? route = null, [FromQuery] string? trigger = null)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var normalizedProviderId = providerId.ToLowerInvariant();
        var userId = await GetCurrentUserIdAsync();
        var safeReturnRoute = SanitizeReturnUrl(route, "/");
        var codeVerifier = AniListApiClient.GenerateCodeVerifier();
        var codeChallenge = AniListApiClient.BuildCodeChallenge(codeVerifier);

        var state = new MediaProviderOAuthState
        {
            UserId = userId,
            ProviderId = normalizedProviderId,
            ReturnUrl = safeReturnRoute,
            Trigger = string.IsNullOrWhiteSpace(trigger) ? "media-provider-connect" : trigger,
            CodeVerifier = codeVerifier,
            CreatedAtTicksUtc = DateTime.UtcNow.Ticks
        };

        var redirectUri = _urlResolver.GetCallbackUrl($"api/media/providers/{normalizedProviderId}/callback");
        var protectedState = _stateProtector.Protect(JsonSerializer.Serialize(state));
        var provider = _mediaProviderRegistry.GetRequired(normalizedProviderId);
        var authorizationUrl = provider.GetAuthorizationUrl(redirectUri, protectedState, codeChallenge);

        return Redirect(authorizationUrl);
    }

    [HttpGet("providers/{providerId}/callback")]
    [AllowAnonymous]
    public async Task<ActionResult> Callback(string providerId, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        var normalizedProviderId = providerId.ToLowerInvariant();
        var frontendUrl = _urlResolver.GetFrontendUrl();

        if (!_mediaProviderRegistry.IsSupported(normalizedProviderId))
        {
            return Redirect($"{frontendUrl}/?error=media_provider_not_supported");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return Redirect($"{frontendUrl}/?error=oauth_denied&provider={normalizedProviderId}");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return Redirect($"{frontendUrl}/?error=invalid_callback&provider={normalizedProviderId}");
        }

        try
        {
            var payloadJson = _stateProtector.Unprotect(state);
            var payload = JsonSerializer.Deserialize<MediaProviderOAuthState>(payloadJson);
            if (payload is null || payload.UserId <= 0 || !string.Equals(payload.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return Redirect($"{frontendUrl}/?error=invalid_state&provider={normalizedProviderId}");
            }

            var createdAt = new DateTime(payload.CreatedAtTicksUtc, DateTimeKind.Utc);
            if (DateTime.UtcNow - createdAt > TimeSpan.FromMinutes(10))
            {
                return Redirect($"{frontendUrl}/?error=state_expired&provider={normalizedProviderId}");
            }

            var redirectUri = _urlResolver.GetCallbackUrl($"api/media/providers/{normalizedProviderId}/callback");
            var provider = _mediaProviderRegistry.GetRequired(normalizedProviderId);
            await provider.ExchangeCodeAndSaveAsync(payload.UserId, code, redirectUri, payload.CodeVerifier, cancellationToken);

            var separator = payload.ReturnUrl.Contains('?') ? '&' : '?';
            return Redirect($"{frontendUrl}{payload.ReturnUrl}{separator}connected=true&provider={normalizedProviderId}&trigger={Uri.EscapeDataString(payload.Trigger)}");
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "AniList media provider callback failed.");
            return Redirect($"{frontendUrl}/?error=exchange_failed&provider={normalizedProviderId}");
        }
    }

    [HttpPost("providers/{providerId}/disconnect")]
    public async Task<ActionResult> Disconnect(string providerId, CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        await provider.DisconnectAsync(userId, cancellationToken);
        return Ok(new { message = $"{provider.ProviderId} disconnected" });
    }

    [HttpPost("providers/{providerId}/import")]
    public async Task<ActionResult<MediaImportDto>> Import(string providerId, CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var account = await provider.GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            return BadRequest(new { error = $"{provider.ProviderId} is not connected." });
        }

        var importResult = await provider.ImportLibraryAsync(userId, cancellationToken);
        var persisted = await _mediaLibraryImportService.ImportAsync(userId, account, importResult, cancellationToken);

        return Ok(new MediaImportDto
        {
            ProviderId = persisted.ProviderId,
            ImportedCount = persisted.ImportedCount,
            CreatedTitles = persisted.CreatedTitles,
            CreatedEntries = persisted.CreatedEntries,
            UpdatedEntries = persisted.UpdatedEntries,
            ImportedAt = persisted.ImportedAt
        });
    }

    [HttpGet("providers/{providerId}/search")]
    public async Task<ActionResult<IReadOnlyList<MediaProviderSearchResultDto>>> Search(
        string providerId,
        [FromQuery] string query,
        [FromQuery] List<string>? mediaKinds,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { error = "Search query is required." });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var results = await provider.SearchAsync(
            userId,
            new MediaCatalogSearchRequest
            {
                Query = query,
                MediaKinds = mediaKinds ?? [],
                Limit = Math.Clamp(limit, 1, 50)
            },
            cancellationToken);

        return Ok(results.Select(MapSearchResult).ToList());
    }

    [HttpGet("providers/{providerId}/titles/{providerMediaId}")]
    public async Task<ActionResult<MediaProviderTitleDetailsDto>> GetTitleDetails(string providerId, string providerMediaId, CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var details = await provider.GetTitleDetailsAsync(userId, providerMediaId, cancellationToken);
        return details is null ? NotFound() : Ok(MapTitleDetails(details));
    }

    [HttpGet("providers/{providerId}/titles/{providerMediaId}/release")]
    public async Task<ActionResult<MediaReleaseMetadataDto>> GetReleaseMetadata(string providerId, string providerMediaId, CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var details = await provider.GetReleaseMetadataAsync(userId, providerMediaId, cancellationToken);
        return details is null ? NotFound() : Ok(MapReleaseMetadata(details));
    }

    [HttpPost("library/{libraryEntryId:guid}/progress")]
    public async Task<ActionResult> UpdateProgress(Guid libraryEntryId, [FromBody] MediaProgressUpdateDto request, CancellationToken cancellationToken)
    {
        if (request.ProgressEpisodes is null && request.ProgressChapters is null && request.ProgressVolumes is null)
        {
            return BadRequest(new { error = "At least one progress field is required." });
        }

        var userId = await GetCurrentUserIdAsync();
        var entry = await LoadLibraryEntryAsync(libraryEntryId, userId, cancellationToken);
        if (entry is null)
        {
            return NotFound(new { error = "Media library entry not found." });
        }

        if (entry.ConnectedServiceAccountId is null)
        {
            return Conflict(new { error = "This media entry is disconnected from its provider account." });
        }

        var operation = await _mediaProviderOperationProcessor.EnqueueProgressUpdateAsync(
            userId,
            entry,
            new MediaProgressUpdateRequest
            {
                ProviderMediaId = entry.ProviderMediaId,
                ProgressEpisodes = request.ProgressEpisodes,
                ProgressChapters = request.ProgressChapters,
                ProgressVolumes = request.ProgressVolumes,
                LastKnownRemoteUpdateAt = entry.LastRemoteUpdateAt
            },
            cancellationToken);

        var result = await _mediaProviderOperationProcessor.ProcessOperationAsync(operation.Id, cancellationToken);
        return BuildOperationResult(result);
    }

    [HttpPost("library/{libraryEntryId:guid}/status")]
    public async Task<ActionResult> UpdateStatus(Guid libraryEntryId, [FromBody] MediaStatusUpdateDto request, CancellationToken cancellationToken)
    {
        var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            MediaLibraryStatuses.Current,
            MediaLibraryStatuses.Planned,
            MediaLibraryStatuses.Paused,
            MediaLibraryStatuses.Completed,
            MediaLibraryStatuses.Dropped
        };

        if (!allowedStatuses.Contains(request.Status))
        {
            return BadRequest(new { error = "Unsupported media status." });
        }

        var userId = await GetCurrentUserIdAsync();
        var entry = await LoadLibraryEntryAsync(libraryEntryId, userId, cancellationToken);
        if (entry is null)
        {
            return NotFound(new { error = "Media library entry not found." });
        }

        if (entry.ConnectedServiceAccountId is null)
        {
            return Conflict(new { error = "This media entry is disconnected from its provider account." });
        }

        var operation = await _mediaProviderOperationProcessor.EnqueueStatusUpdateAsync(
            userId,
            entry,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = entry.ProviderMediaId,
                Status = request.Status,
                LastKnownRemoteUpdateAt = entry.LastRemoteUpdateAt
            },
            cancellationToken);

        var result = await _mediaProviderOperationProcessor.ProcessOperationAsync(operation.Id, cancellationToken);
        return BuildOperationResult(result);
    }

    private static ActionResult BuildOperationResult(MediaProviderOperationExecutionResult result)
    {
        return result.Outcome switch
        {
            MediaProviderOperationExecutionOutcome.Succeeded => new NoContentResult(),
            MediaProviderOperationExecutionOutcome.Queued => new AcceptedResult(),
            MediaProviderOperationExecutionOutcome.Failed => new ObjectResult(new
            {
                error = string.IsNullOrWhiteSpace(result.LastError)
                    ? "Media provider operation failed."
                    : result.LastError
            })
            {
                StatusCode = StatusCodes.Status502BadGateway
            },
            _ => new AcceptedResult()
        };
    }

    private static MediaProviderSearchResultDto MapSearchResult(MediaProviderSearchResult result)
    {
        return new MediaProviderSearchResultDto
        {
            ProviderId = result.ProviderId,
            ProviderMediaId = result.ProviderMediaId,
            Title = result.Title,
            NativeTitle = result.NativeTitle,
            MediaKind = result.MediaKind,
            Synopsis = result.Synopsis,
            PosterUrl = result.PosterUrl,
            BackgroundUrl = result.BackgroundUrl,
            StartYear = result.StartYear,
            EpisodeCount = result.EpisodeCount,
            ChapterCount = result.ChapterCount,
            VolumeCount = result.VolumeCount,
            PrimaryProgressDimension = result.PrimaryProgressDimension,
            ReleaseStatusDimension = result.ReleaseStatusDimension
        };
    }

    private static MediaProviderTitleDetailsDto MapTitleDetails(MediaProviderTitleDetails details)
    {
        return new MediaProviderTitleDetailsDto
        {
            ProviderId = details.ProviderId,
            ProviderMediaId = details.ProviderMediaId,
            Title = details.Title,
            NativeTitle = details.NativeTitle,
            MediaKind = details.MediaKind,
            Synopsis = details.Synopsis,
            PosterUrl = details.PosterUrl,
            BackgroundUrl = details.BackgroundUrl,
            StartYear = details.StartYear,
            EpisodeCount = details.EpisodeCount,
            ChapterCount = details.ChapterCount,
            VolumeCount = details.VolumeCount,
            PrimaryProgressDimension = details.PrimaryProgressDimension,
            ReleaseStatusDimension = details.ReleaseStatusDimension
        };
    }

    private static MediaReleaseMetadataDto MapReleaseMetadata(MediaReleaseMetadata metadata)
    {
        return new MediaReleaseMetadataDto
        {
            ProviderId = metadata.ProviderId,
            ProviderMediaId = metadata.ProviderMediaId,
            ReleaseStatusDimension = metadata.ReleaseStatusDimension,
            ReleasedCount = metadata.ReleasedCount,
            TotalKnownCount = metadata.TotalKnownCount,
            NextReleaseAt = metadata.NextReleaseAt,
            NextReleaseLabel = metadata.NextReleaseLabel
        };
    }

    private async Task<MediaLibraryEntry?> LoadLibraryEntryAsync(Guid libraryEntryId, int userId, CancellationToken cancellationToken)
    {
        return await _dbContext.MediaLibraryEntries
            .Include(entry => entry.MediaTitle)
            .FirstOrDefaultAsync(entry => entry.Id == libraryEntryId && entry.UserId == userId, cancellationToken);
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        return user.Id;
    }

    private static string SanitizeReturnUrl(string? route, string defaultPath)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return defaultPath;
        }

        if (!route.StartsWith('/') || route.StartsWith("//") || route.Contains("://") || route.Contains('\\'))
        {
            return defaultPath;
        }

        return route;
    }
}
