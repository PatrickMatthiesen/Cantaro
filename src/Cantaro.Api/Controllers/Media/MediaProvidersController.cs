using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
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
    MediaLibraryImportQueue mediaLibraryImportQueue,
    MediaProviderOperationProcessor mediaProviderOperationProcessor,
    UserManager<User> userManager,
    ILogger<MediaProvidersController> logger,
    IDataProtectionProvider dataProtectionProvider,
    IFrontendUrlResolver urlResolver) : ControllerBase
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMediaProviderRegistry _mediaProviderRegistry = mediaProviderRegistry;
    private readonly MediaLibraryImportQueue _mediaLibraryImportQueue = mediaLibraryImportQueue;
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

        var protectedState = _stateProtector.Protect(JsonSerializer.Serialize(state));

        try
        {
            var provider = _mediaProviderRegistry.GetRequired(normalizedProviderId);
            var redirectUri = provider.ResolveRedirectUri(
                _urlResolver.GetCallbackUrls($"api/media/providers/{normalizedProviderId}/callback"));
            var authorizationUrl = provider.GetAuthorizationUrl(redirectUri, protectedState, codeChallenge);
            return Redirect(authorizationUrl);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Media provider {ProviderId} is not configured for OAuth connect.", normalizedProviderId);
            var frontendUrl = _urlResolver.GetFrontendUrl();
            var separator = safeReturnRoute.Contains('?') ? '&' : '?';
            return Redirect($"{frontendUrl}{safeReturnRoute}{separator}error=provider_not_configured&provider={normalizedProviderId}");
        }
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

            var provider = _mediaProviderRegistry.GetRequired(normalizedProviderId);
            var redirectUri = provider.ResolveRedirectUri(
                _urlResolver.GetCallbackUrls($"api/media/providers/{normalizedProviderId}/callback"));
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
    public async Task<ActionResult<MediaImportRequestDto>> Import(string providerId, CancellationToken cancellationToken)
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

        var workItem = await _mediaLibraryImportQueue.EnqueueAsync(userId, provider.ProviderId, cancellationToken);

        return Accepted(new MediaImportRequestDto
        {
            ProviderId = workItem.ProviderId,
            ImportId = workItem.ImportId,
            Status = "queued"
        });
    }

    [HttpGet("providers/{providerId}/import/events")]
    public async Task<IResult> StreamImportEvents(
        string providerId,
        [FromQuery] Guid? importId,
        CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return TypedResults.NotFound();
        }

        var userId = await GetCurrentUserIdAsync();
        return TypedResults.ServerSentEvents(
            ReadImportEvents(providerId, userId, importId, cancellationToken));
    }

    private async IAsyncEnumerable<SseItem<MediaLibraryImportEventDto>> ReadImportEvents(
        string providerId,
        int userId,
        Guid? importId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reader = _mediaLibraryImportQueue.Subscribe(userId, out var subscriptionId);

        try
        {
            if (importId.HasValue
                && _mediaLibraryImportQueue.TryGetLatestEvent(importId.Value, out var latestEvent)
                && latestEvent is not null
                && string.Equals(latestEvent.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                yield return new SseItem<MediaLibraryImportEventDto>(latestEvent);
                if (IsTerminalImportStatus(latestEvent.Status))
                {
                    yield break;
                }
            }

            await foreach (var importEvent in reader.ReadAllAsync(cancellationToken))
            {
                if (!string.Equals(importEvent.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (importId.HasValue && importEvent.ImportId != importId.Value)
                {
                    continue;
                }

                yield return new SseItem<MediaLibraryImportEventDto>(importEvent);
                if (importId.HasValue && IsTerminalImportStatus(importEvent.Status))
                {
                    yield break;
                }
            }
        }
        finally
        {
            _mediaLibraryImportQueue.Unsubscribe(userId, subscriptionId);
        }
    }

    private static bool IsTerminalImportStatus(string status)
    {
        return status is "completed" or "failed";
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

        var libraryStates = await GetLibraryStatesAsync(userId, provider.ProviderId, results.Select(result => result.ProviderMediaId), cancellationToken);

        return Ok(results.Select(result => MapSearchResult(result, libraryStates)).ToList());
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
        if (details is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var title = await FindOrCreateMediaTitleAsync(
            provider.ProviderId,
            providerMediaId,
            details,
            now,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var libraryState = await GetLibraryStateAsync(userId, title.Id, cancellationToken);
        return Ok(MapTitleDetails(details, title.Id, libraryState));
    }

    [HttpPost("providers/{providerId}/titles/{providerMediaId}/library")]
    public async Task<ActionResult<MediaCatalogAddResultDto>> AddTitleToLibrary(
        string providerId,
        string providerMediaId,
        [FromBody] MediaCatalogAddRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!_mediaProviderRegistry.IsSupported(providerId))
        {
            return NotFound(new { error = $"Media provider '{providerId}' is not implemented" });
        }

        var allowedStatuses = GetWritableMediaStatuses();
        if (!allowedStatuses.Contains(request.Status))
        {
            return BadRequest(new { error = "Unsupported media status." });
        }

        var userId = await GetCurrentUserIdAsync();
        var provider = _mediaProviderRegistry.GetRequired(providerId);
        var existingTitleId = await _dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => link.Provider == provider.ProviderId && link.ExternalId == providerMediaId)
            .Select(link => (Guid?)link.MediaTitleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingTitleId is not null)
        {
            var canonicalExistingEntry = await FindPreferredLibraryEntryAsync(userId, existingTitleId.Value, cancellationToken);
            if (canonicalExistingEntry is not null)
            {
                return Ok(new MediaCatalogAddResultDto
                {
                    LibraryEntryId = canonicalExistingEntry.Id,
                    MediaTitleId = canonicalExistingEntry.MediaTitleId,
                    Status = canonicalExistingEntry.NormalizedStatus
                });
            }
        }

        var account = await provider.GetConnectedAccountAsync(userId, cancellationToken);
        if (account is null)
        {
            return BadRequest(new { error = $"{provider.ProviderId} is not connected." });
        }

        var details = await provider.GetTitleDetailsAsync(userId, providerMediaId, cancellationToken);
        if (details is null)
        {
            return NotFound(new { error = "Provider title not found." });
        }

        var now = DateTimeOffset.UtcNow;
        var title = await FindOrCreateMediaTitleAsync(provider.ProviderId, providerMediaId, details, now, cancellationToken);
        var existingEntry = await FindPreferredLibraryEntryAsync(userId, title.Id, cancellationToken);

        if (existingEntry is not null)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new MediaCatalogAddResultDto
            {
                LibraryEntryId = existingEntry.Id,
                MediaTitleId = existingEntry.MediaTitleId,
                Status = existingEntry.NormalizedStatus
            });
        }

        var mutationResult = await provider.UpdateStatusAsync(
            userId,
            new MediaStatusUpdateRequest
            {
                ProviderMediaId = providerMediaId,
                Status = request.Status
            },
            cancellationToken);
        var entry = new MediaLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MediaTitleId = title.Id,
            ConnectedServiceAccountId = account.Id,
            Provider = provider.ProviderId,
            ProviderAccountId = account.ExternalAccountId,
            ProviderMediaId = providerMediaId,
            NormalizedStatus = request.Status,
            RawStatus = mutationResult.RawStatus,
            ProgressEpisodes = details.PrimaryProgressDimension == MediaProgressDimensions.Episode ? 0 : null,
            ProgressChapters = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter ? 0 : null,
            ProgressVolumes = details.PrimaryProgressDimension == MediaProgressDimensions.Volume ? 0 : null,
            LastSyncedAt = now,
            LastRemoteUpdateAt = mutationResult.LastRemoteUpdateAt ?? now,
            LastLocalEditAt = now,
            LastMutationSource = MediaMutationSources.UserStatusUpdate,
            RawMetadata = mutationResult.RawMetadata ?? details.RawMetadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.MediaLibraryEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new MediaCatalogAddResultDto
        {
            LibraryEntryId = entry.Id,
            MediaTitleId = title.Id,
            Status = entry.NormalizedStatus
        });
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

        ApplyLocalProgressUpdate(entry, request);

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

        ApplyLocalStatusUpdate(entry, request);

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

    private static void ApplyLocalProgressUpdate(MediaLibraryEntry entry, MediaProgressUpdateDto request)
    {
        var now = DateTimeOffset.UtcNow;
        entry.ProgressEpisodes = request.ProgressEpisodes ?? entry.ProgressEpisodes;
        entry.ProgressChapters = request.ProgressChapters ?? entry.ProgressChapters;
        entry.ProgressVolumes = request.ProgressVolumes ?? entry.ProgressVolumes;
        entry.LastLocalEditAt = now;
        entry.LastMutationSource = MediaMutationSources.UserProgressUpdate;
        entry.UpdatedAt = now;
    }

    private static void ApplyLocalStatusUpdate(MediaLibraryEntry entry, MediaStatusUpdateDto request)
    {
        var now = DateTimeOffset.UtcNow;
        entry.NormalizedStatus = request.Status;
        entry.LastLocalEditAt = now;
        entry.LastMutationSource = MediaMutationSources.UserStatusUpdate;
        entry.UpdatedAt = now;
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

    private static MediaProviderSearchResultDto MapSearchResult(
        MediaProviderSearchResult result,
        IReadOnlyDictionary<string, MediaCatalogLibraryStateDto> libraryStates)
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
            ReleaseStatusDimension = result.ReleaseStatusDimension,
            LibraryState = libraryStates.TryGetValue(result.ProviderMediaId, out var state)
                ? state
                : new MediaCatalogLibraryStateDto()
        };
    }

    private static MediaProviderTitleDetailsDto MapTitleDetails(
        MediaProviderTitleDetails details,
        Guid mediaTitleId,
        MediaCatalogLibraryStateDto? libraryState = null)
    {
        return new MediaProviderTitleDetailsDto
        {
            MediaTitleId = mediaTitleId,
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
            ReleaseStatusDimension = details.ReleaseStatusDimension,
            AvailabilityLinks = details.AvailabilityLinks.Select(link => new MediaProviderAvailabilityLinkDto
            {
                ServiceId = link.ServiceId,
                DisplayName = link.DisplayName,
                Url = link.Url,
                AvailabilityKind = link.AvailabilityKind,
                Notes = link.Notes,
                IconUrl = link.IconUrl
            }).ToList(),
            Characters = details.Characters.Select(character => new MediaProviderCharacterCreditDto
            {
                CharacterId = character.CharacterId,
                Name = character.Name,
                ImageUrl = character.ImageUrl,
                Role = character.Role,
                ProviderUrl = character.ProviderUrl,
                Order = character.Order
            }).ToList(),
            LibraryState = libraryState ?? new MediaCatalogLibraryStateDto()
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

    private async Task<IReadOnlyDictionary<string, MediaCatalogLibraryStateDto>> GetLibraryStatesAsync(
        int userId,
        string providerId,
        IEnumerable<string> providerMediaIds,
        CancellationToken cancellationToken)
    {
        var mediaIds = providerMediaIds.Distinct(StringComparer.Ordinal).ToList();
        if (mediaIds.Count == 0)
        {
            return new Dictionary<string, MediaCatalogLibraryStateDto>(StringComparer.Ordinal);
        }

        var links = await _dbContext.MediaProviderLinks
            .AsNoTracking()
            .Where(link => link.Provider == providerId && mediaIds.Contains(link.ExternalId))
            .Select(link => new { link.ExternalId, link.MediaTitleId })
            .ToListAsync(cancellationToken);

        var titleIds = links.Select(link => link.MediaTitleId).Distinct().ToList();
        if (titleIds.Count == 0)
        {
            return new Dictionary<string, MediaCatalogLibraryStateDto>(StringComparer.Ordinal);
        }

        var entriesByTitleId = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && titleIds.Contains(entry.MediaTitleId))
            .ToListAsync(cancellationToken);

        var preferredEntries = entriesByTitleId
            .OrderByDescending(entry => entry.ConnectedServiceAccountId != null)
            .ThenByDescending(entry => entry.UpdatedAt)
            .GroupBy(entry => entry.MediaTitleId)
            .ToDictionary(group => group.Key, group => group.First());

        return links
            .Where(link => preferredEntries.ContainsKey(link.MediaTitleId))
            .ToDictionary(
                link => link.ExternalId,
                link => MapLibraryState(preferredEntries[link.MediaTitleId]),
                StringComparer.Ordinal);
    }

    private async Task<MediaCatalogLibraryStateDto> GetLibraryStateAsync(
        int userId,
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.MediaTitleId == mediaTitleId)
            .ToListAsync(cancellationToken);
        var entry = entries
            .OrderByDescending(item => item.ConnectedServiceAccountId != null)
            .ThenByDescending(item => item.UpdatedAt)
            .FirstOrDefault();

        return entry is null ? new MediaCatalogLibraryStateDto() : MapLibraryState(entry);
    }

    private async Task<MediaLibraryEntry?> FindPreferredLibraryEntryAsync(
        int userId,
        Guid mediaTitleId,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.MediaTitleId == mediaTitleId)
            .ToListAsync(cancellationToken);

        return entries
            .OrderByDescending(entry => entry.ConnectedServiceAccountId != null)
            .ThenByDescending(entry => entry.UpdatedAt)
            .FirstOrDefault();
    }

    private static MediaCatalogLibraryStateDto MapLibraryState(MediaLibraryEntry entry)
    {
        return new MediaCatalogLibraryStateDto
        {
            IsInLibrary = true,
            LibraryEntryId = entry.Id,
            MediaTitleId = entry.MediaTitleId,
            NormalizedStatus = entry.NormalizedStatus,
            ProgressEpisodes = entry.ProgressEpisodes,
            ProgressChapters = entry.ProgressChapters,
            ProgressVolumes = entry.ProgressVolumes
        };
    }

    private async Task<MediaTitle> FindOrCreateMediaTitleAsync(
        string providerId,
        string providerMediaId,
        MediaProviderTitleDetails details,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingLink = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
            .FirstOrDefaultAsync(link => link.Provider == providerId && link.ExternalId == providerMediaId, cancellationToken);

        if (existingLink?.MediaTitle is { } linkedTitle)
        {
            ApplyProviderDetails(linkedTitle, details, now);
            existingLink.RawMetadata = details.RawMetadata ?? existingLink.RawMetadata;
            existingLink.LastVerifiedAt = now;
            existingLink.UpdatedAt = now;
            return linkedTitle;
        }

        var title = new MediaTitle
        {
            Id = Guid.NewGuid(),
            CanonicalTitle = details.Title,
            SortTitle = details.Title,
            OriginalTitle = details.NativeTitle,
            MediaKind = details.MediaKind,
            Synopsis = details.Synopsis,
            StartYear = details.StartYear,
            EpisodeCount = details.EpisodeCount,
            ChapterCount = details.ChapterCount,
            VolumeCount = details.VolumeCount,
            SupportsEpisodeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Episode,
            SupportsChapterProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter,
            SupportsVolumeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Volume,
            IsCompletionOnly = details.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly,
            PrimaryProgressDimension = details.PrimaryProgressDimension,
            ReleaseStatusDimension = details.ReleaseStatusDimension,
            CanonicalMetadata = details.RawMetadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.MediaTitles.Add(title);
        _dbContext.MediaProviderLinks.Add(new MediaProviderLink
        {
            Id = Guid.NewGuid(),
            MediaTitleId = title.Id,
            Provider = providerId,
            ExternalId = providerMediaId,
            LinkSource = MediaMappingSources.Imported,
            LastVerifiedAt = now,
            RawMetadata = details.RawMetadata,
            CreatedAt = now,
            UpdatedAt = now
        });

        return title;
    }

    private static void ApplyProviderDetails(MediaTitle title, MediaProviderTitleDetails details, DateTimeOffset now)
    {
        title.CanonicalTitle = details.Title;
        title.SortTitle = details.Title;
        title.OriginalTitle = details.NativeTitle ?? title.OriginalTitle;
        title.MediaKind = details.MediaKind;
        title.Synopsis = details.Synopsis ?? title.Synopsis;
        title.StartYear = details.StartYear ?? title.StartYear;
        title.EpisodeCount = details.EpisodeCount ?? title.EpisodeCount;
        title.ChapterCount = details.ChapterCount ?? title.ChapterCount;
        title.VolumeCount = details.VolumeCount ?? title.VolumeCount;
        title.SupportsEpisodeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Episode;
        title.SupportsChapterProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Chapter;
        title.SupportsVolumeProgress = details.PrimaryProgressDimension == MediaProgressDimensions.Volume;
        title.IsCompletionOnly = details.PrimaryProgressDimension == MediaProgressDimensions.CompletionOnly;
        title.PrimaryProgressDimension = details.PrimaryProgressDimension;
        title.ReleaseStatusDimension = details.ReleaseStatusDimension;
        title.CanonicalMetadata = details.RawMetadata ?? title.CanonicalMetadata;
        title.UpdatedAt = now;
    }

    private static HashSet<string> GetWritableMediaStatuses()
    {
        return new HashSet<string>(StringComparer.Ordinal)
        {
            MediaLibraryStatuses.Current,
            MediaLibraryStatuses.Planned,
            MediaLibraryStatuses.Paused,
            MediaLibraryStatuses.Completed,
            MediaLibraryStatuses.Dropped
        };
    }

    private async Task<int> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            throw new UnauthorizedAccessException("User not authenticated");
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
