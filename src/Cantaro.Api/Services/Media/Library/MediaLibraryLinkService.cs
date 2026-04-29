using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public enum MediaLinkResultKind
{
    Success,
    EntryNotFound,
    /// <summary>
    /// The link already exists and is correct (idempotent success).
    /// </summary>
    AlreadyLinked,
    /// <summary>
    /// Another canonical title is already associated with the given provider/external ID.
    /// The caller must set ForceRelink=true to proceed.
    /// </summary>
    ConflictingTitle,
    ProviderLinkNotFound
}

public class MediaLinkResult
{
    public MediaLinkResultKind Kind { get; init; }
    public Guid? ConflictingMediaTitleId { get; init; }
    public string? ConflictingCanonicalTitle { get; init; }

    public static MediaLinkResult Success() => new() { Kind = MediaLinkResultKind.Success };
    public static MediaLinkResult AlreadyLinked() => new() { Kind = MediaLinkResultKind.AlreadyLinked };
    public static MediaLinkResult EntryNotFound() => new() { Kind = MediaLinkResultKind.EntryNotFound };
    public static MediaLinkResult ProviderLinkNotFound() => new() { Kind = MediaLinkResultKind.ProviderLinkNotFound };

    public static MediaLinkResult Conflict(Guid conflictingId, string conflictingTitle) => new()
    {
        Kind = MediaLinkResultKind.ConflictingTitle,
        ConflictingMediaTitleId = conflictingId,
        ConflictingCanonicalTitle = conflictingTitle
    };
}

/// <summary>
/// Handles manual linking and unlinking of a user's media library entry to a provider catalog entry.
/// </summary>
public class MediaLibraryLinkService(
    ApplicationDbContext dbContext,
    ILogger<MediaLibraryLinkService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaLibraryLinkService> _logger = logger;

    /// <summary>
    /// Links the <see cref="MediaTitle"/> of a library entry to a provider catalog entry identified by
    /// <paramref name="providerId"/>/<paramref name="providerMediaId"/>.
    ///
    /// No-silent-reassignment rule: if the given (provider, externalId) pair is already mapped to a
    /// <em>different</em> canonical title, a <see cref="MediaLinkResultKind.ConflictingTitle"/> result is
    /// returned and no changes are persisted unless <paramref name="forceRelink"/> is true.
    /// </summary>
    public async Task<MediaLinkResult> LinkProviderAsync(
        int userId,
        Guid libraryEntryId,
        string providerId,
        string providerMediaId,
        bool forceRelink,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .Include(e => e.MediaTitle)
            .FirstOrDefaultAsync(e => e.Id == libraryEntryId && e.UserId == userId, cancellationToken);

        if (entry is null || entry.MediaTitle is null)
        {
            return MediaLinkResult.EntryNotFound();
        }

        var normalizedProvider = providerId.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        // Check whether a link for (provider, externalId) already exists globally.
        var existingLinkForExternalId = await _dbContext.MediaProviderLinks
            .Include(l => l.MediaTitle)
            .FirstOrDefaultAsync(
                l => l.Provider == normalizedProvider && l.ExternalId == providerMediaId,
                cancellationToken);

        if (existingLinkForExternalId is not null)
        {
            if (existingLinkForExternalId.MediaTitleId == entry.MediaTitleId)
            {
                // Idempotent: the correct link already exists. Refresh the verification timestamp.
                existingLinkForExternalId.LastVerifiedAt = now;
                existingLinkForExternalId.UpdatedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return MediaLinkResult.AlreadyLinked();
            }

            // No-silent-reassignment: different title is already using this external ID.
            if (!forceRelink)
            {
                return MediaLinkResult.Conflict(
                    existingLinkForExternalId.MediaTitleId,
                    existingLinkForExternalId.MediaTitle?.CanonicalTitle ?? existingLinkForExternalId.MediaTitleId.ToString());
            }

            // ForceRelink: move the existing link to the entry's title.
            _logger.LogWarning(
                "Force-relinking provider {Provider}/{ExternalId} from MediaTitle {OldTitleId} to {NewTitleId} by user {UserId}.",
                normalizedProvider, providerMediaId, existingLinkForExternalId.MediaTitleId, entry.MediaTitleId, userId);

            existingLinkForExternalId.MediaTitleId = entry.MediaTitleId;
            existingLinkForExternalId.LinkSource = MediaMappingSources.UserConfirmed;
            existingLinkForExternalId.LinkedByUserId = userId;
            existingLinkForExternalId.LastVerifiedAt = now;
            existingLinkForExternalId.UpdatedAt = now;
        }

        // Check whether this title already has a link for this provider (replace it).
        var existingLinkForTitleProvider = await _dbContext.MediaProviderLinks
            .FirstOrDefaultAsync(
                l => l.MediaTitleId == entry.MediaTitleId && l.Provider == normalizedProvider,
                cancellationToken);

        if (existingLinkForExternalId is not null
            && existingLinkForTitleProvider is not null
            && existingLinkForTitleProvider.Id != existingLinkForExternalId.Id)
        {
            _dbContext.MediaProviderLinks.Remove(existingLinkForTitleProvider);
            existingLinkForTitleProvider = null;
        }

        if (existingLinkForTitleProvider is not null && existingLinkForTitleProvider.ExternalId != providerMediaId)
        {
            // Replace the existing link for this title+provider with the new external ID.
            _logger.LogInformation(
                "Updating provider link for MediaTitle {TitleId} on {Provider} from ExternalId {OldExternalId} to {NewExternalId} by user {UserId}.",
                entry.MediaTitleId, normalizedProvider, existingLinkForTitleProvider.ExternalId, providerMediaId, userId);

            existingLinkForTitleProvider.ExternalId = providerMediaId;
            existingLinkForTitleProvider.LinkSource = MediaMappingSources.UserConfirmed;
            existingLinkForTitleProvider.LinkedByUserId = userId;
            existingLinkForTitleProvider.LastVerifiedAt = now;
            existingLinkForTitleProvider.UpdatedAt = now;
        }
        else if (existingLinkForExternalId is null && existingLinkForTitleProvider is null)
        {
            // No link exists yet — create one.
            var link = new MediaProviderLink
            {
                Id = Guid.NewGuid(),
                MediaTitleId = entry.MediaTitleId,
                Provider = normalizedProvider,
                ExternalId = providerMediaId,
                LinkSource = MediaMappingSources.UserConfirmed,
                LinkedByUserId = userId,
                LastVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.MediaProviderLinks.Add(link);
        }

        // If the library entry's own provider matches, keep ProviderMediaId in sync.
        if (string.Equals(entry.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase)
            && entry.ProviderMediaId != providerMediaId)
        {
            entry.ProviderMediaId = providerMediaId;
            entry.LastMutationSource = MediaMutationSources.UserProgressUpdate;
            entry.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} linked library entry {EntryId} (MediaTitle {TitleId}) to {Provider}/{ExternalId}.",
            userId, libraryEntryId, entry.MediaTitleId, normalizedProvider, providerMediaId);

        return MediaLinkResult.Success();
    }

    /// <summary>
    /// Removes the <see cref="MediaProviderLink"/> between the entry's <see cref="MediaTitle"/> and the
    /// given provider. Returns false when no such link exists.
    /// </summary>
    public async Task<MediaLinkResult> UnlinkProviderAsync(
        int userId,
        Guid libraryEntryId,
        string providerId,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .FirstOrDefaultAsync(e => e.Id == libraryEntryId && e.UserId == userId, cancellationToken);

        if (entry is null)
        {
            return MediaLinkResult.EntryNotFound();
        }

        var normalizedProvider = providerId.ToLowerInvariant();

        var link = await _dbContext.MediaProviderLinks
            .FirstOrDefaultAsync(
                l => l.MediaTitleId == entry.MediaTitleId && l.Provider == normalizedProvider,
                cancellationToken);

        if (link is null)
        {
            return MediaLinkResult.ProviderLinkNotFound();
        }

        _dbContext.MediaProviderLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} unlinked MediaTitle {TitleId} from provider {Provider}.",
            userId, entry.MediaTitleId, normalizedProvider);

        return MediaLinkResult.Success();
    }
}
