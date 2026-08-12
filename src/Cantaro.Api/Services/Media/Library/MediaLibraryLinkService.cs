using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public enum MediaLinkResultKind
{
    Success,
    EntryNotFound,
    AlreadyLinked,
    ConflictingTitle,
    ReplacementConfirmationRequired,
    LinkInUse,
    ProviderLinkNotFound
}

public class MediaLinkResult
{
    public MediaLinkResultKind Kind { get; init; }
    public Guid? ConflictingMediaTitleId { get; init; }
    public string? ConflictingCanonicalTitle { get; init; }
    public string? CurrentProviderMediaId { get; init; }

    public static MediaLinkResult Success() => new() { Kind = MediaLinkResultKind.Success };
    public static MediaLinkResult AlreadyLinked() => new() { Kind = MediaLinkResultKind.AlreadyLinked };
    public static MediaLinkResult EntryNotFound() => new() { Kind = MediaLinkResultKind.EntryNotFound };
    public static MediaLinkResult ProviderLinkNotFound() => new() { Kind = MediaLinkResultKind.ProviderLinkNotFound };
    public static MediaLinkResult ReplacementRequired(string currentProviderMediaId) => new()
    {
        Kind = MediaLinkResultKind.ReplacementConfirmationRequired,
        CurrentProviderMediaId = currentProviderMediaId
    };
    public static MediaLinkResult LinkInUse(string currentProviderMediaId) => new()
    {
        Kind = MediaLinkResultKind.LinkInUse,
        CurrentProviderMediaId = currentProviderMediaId
    };
    public static MediaLinkResult Conflict(Guid conflictingId, string conflictingTitle) => new()
    {
        Kind = MediaLinkResultKind.ConflictingTitle,
        ConflictingMediaTitleId = conflictingId,
        ConflictingCanonicalTitle = conflictingTitle
    };
}

public class MediaLibraryLinkService(
    ApplicationDbContext dbContext,
    ILogger<MediaLibraryLinkService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaLibraryLinkService> _logger = logger;

    public async Task<MediaLinkResult> LinkProviderAsync(
        int userId,
        Guid mediaTitleId,
        string providerId,
        string providerMediaId,
        bool confirmReplacement,
        CancellationToken cancellationToken)
    {
        var canEdit = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.UserId == userId && entry.MediaTitleId == mediaTitleId, cancellationToken);
        if (!canEdit)
        {
            return MediaLinkResult.EntryNotFound();
        }

        var normalizedProvider = providerId.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var externalIdentity = await _dbContext.MediaProviderLinks
            .Include(link => link.MediaTitle)
            .FirstOrDefaultAsync(
                link => link.Provider == normalizedProvider && link.ExternalId == providerMediaId,
                cancellationToken);

        if (externalIdentity is not null)
        {
            if (externalIdentity.MediaTitleId != mediaTitleId)
            {
                return MediaLinkResult.Conflict(
                    externalIdentity.MediaTitleId,
                    externalIdentity.MediaTitle?.CanonicalTitle ?? externalIdentity.MediaTitleId.ToString());
            }

            externalIdentity.LastVerifiedAt = now;
            externalIdentity.UpdatedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MediaLinkResult.AlreadyLinked();
        }

        var currentLink = await _dbContext.MediaProviderLinks
            .FirstOrDefaultAsync(
                link => link.MediaTitleId == mediaTitleId && link.Provider == normalizedProvider,
                cancellationToken);
        if (currentLink is not null)
        {
            if (!confirmReplacement)
            {
                return MediaLinkResult.ReplacementRequired(currentLink.ExternalId);
            }

            var isBound = await _dbContext.MediaLibraryProviderBindings
                .AsNoTracking()
                .AnyAsync(binding => binding.MediaProviderLinkId == currentLink.Id, cancellationToken);
            if (isBound)
            {
                return MediaLinkResult.LinkInUse(currentLink.ExternalId);
            }

            currentLink.ExternalId = providerMediaId;
            currentLink.LinkSource = MediaMappingSources.UserConfirmed;
            currentLink.LinkedByUserId = userId;
            currentLink.LastVerifiedAt = now;
            currentLink.UpdatedAt = now;
        }
        else
        {
            _dbContext.MediaProviderLinks.Add(new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = mediaTitleId,
                Provider = normalizedProvider,
                ExternalId = providerMediaId,
                LinkSource = MediaMappingSources.UserConfirmed,
                LinkedByUserId = userId,
                LastVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "User {UserId} linked MediaTitle {TitleId} to {Provider}/{ExternalId}.",
            userId,
            mediaTitleId,
            normalizedProvider,
            providerMediaId);
        return MediaLinkResult.Success();
    }

    public async Task<MediaLinkResult> UnlinkProviderAsync(
        int userId,
        Guid mediaTitleId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var canEdit = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.UserId == userId && entry.MediaTitleId == mediaTitleId, cancellationToken);
        if (!canEdit)
        {
            return MediaLinkResult.EntryNotFound();
        }

        var normalizedProvider = providerId.Trim().ToLowerInvariant();
        var link = await _dbContext.MediaProviderLinks
            .FirstOrDefaultAsync(
                item => item.MediaTitleId == mediaTitleId && item.Provider == normalizedProvider,
                cancellationToken);
        if (link is null)
        {
            return MediaLinkResult.ProviderLinkNotFound();
        }

        var isBound = await _dbContext.MediaLibraryProviderBindings
            .AsNoTracking()
            .AnyAsync(binding => binding.MediaProviderLinkId == link.Id, cancellationToken);
        if (isBound)
        {
            return MediaLinkResult.LinkInUse(link.ExternalId);
        }

        _dbContext.MediaProviderLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "User {UserId} unlinked MediaTitle {TitleId} from provider {Provider}.",
            userId,
            mediaTitleId,
            normalizedProvider);
        return MediaLinkResult.Success();
    }
}
