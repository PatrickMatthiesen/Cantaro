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
    /// </summary>
    ConflictingTitle,
    /// <summary>
    /// This title already has a different identity for the provider and replacement must be confirmed.
    /// </summary>
    ReplacementConfirmationRequired,
    /// <summary>
    /// The provider identity is used by other library entries and cannot be safely changed or removed.
    /// </summary>
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
    /// No-silent-reassignment rule: a provider identity owned by another canonical title can never be
    /// reassigned through this user-facing operation. Replacing this title's identity for the same provider
    /// requires explicit confirmation and is blocked when other library entries depend on the old identity.
    /// </summary>
    public async Task<MediaLinkResult> LinkProviderAsync(
        int userId,
        Guid libraryEntryId,
        string providerId,
        string providerMediaId,
        bool confirmReplacement,
        CancellationToken cancellationToken)
    {
        var entry = await _dbContext.MediaLibraryEntries
            .Include(e => e.MediaTitle)
            .FirstOrDefaultAsync(e => e.Id == libraryEntryId && e.UserId == userId, cancellationToken);

        if (entry is null || entry.MediaTitle is null)
        {
            return MediaLinkResult.EntryNotFound();
        }

        var normalizedProvider = providerId.Trim().ToLowerInvariant();
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
                if (string.Equals(entry.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase)
                    && entry.ProviderMediaId != providerMediaId)
                {
                    if (!confirmReplacement)
                    {
                        return MediaLinkResult.ReplacementRequired(entry.ProviderMediaId);
                    }

                    var hasOtherIncompatibleEntries = await _dbContext.MediaLibraryEntries
                        .AsNoTracking()
                        .AnyAsync(item =>
                            item.MediaTitleId == entry.MediaTitleId
                            && item.Provider == normalizedProvider
                            && item.ProviderMediaId != providerMediaId
                            && item.Id != entry.Id,
                            cancellationToken);

                    if (hasOtherIncompatibleEntries)
                    {
                        return MediaLinkResult.LinkInUse(entry.ProviderMediaId);
                    }

                    entry.ProviderMediaId = providerMediaId;
                    entry.LastMutationSource = MediaMutationSources.UserProviderIdentityCorrection;
                    entry.UpdatedAt = now;
                }

                // Idempotent: the correct link already exists. Refresh the verification timestamp.
                existingLinkForExternalId.LastVerifiedAt = now;
                existingLinkForExternalId.UpdatedAt = now;
                await _dbContext.SaveChangesAsync(cancellationToken);
                return MediaLinkResult.AlreadyLinked();
            }

            return MediaLinkResult.Conflict(
                existingLinkForExternalId.MediaTitleId,
                existingLinkForExternalId.MediaTitle?.CanonicalTitle ?? existingLinkForExternalId.MediaTitleId.ToString());
        }

        // Check whether this title already has a link for this provider (replace it).
        var existingLinkForTitleProvider = await _dbContext.MediaProviderLinks
            .FirstOrDefaultAsync(
                l => l.MediaTitleId == entry.MediaTitleId && l.Provider == normalizedProvider,
                cancellationToken);

        if (existingLinkForTitleProvider is not null && existingLinkForTitleProvider.ExternalId != providerMediaId)
        {
            if (!confirmReplacement)
            {
                return MediaLinkResult.ReplacementRequired(existingLinkForTitleProvider.ExternalId);
            }

            var incompatibleEntries = await _dbContext.MediaLibraryEntries
                .AsNoTracking()
                .Where(item =>
                    item.MediaTitleId == entry.MediaTitleId
                    && item.Provider == normalizedProvider
                    && item.ProviderMediaId != providerMediaId)
                .Select(item => new { item.Id, item.ProviderMediaId })
                .ToListAsync(cancellationToken);

            if (incompatibleEntries.Any(item =>
                    item.Id != entry.Id || item.ProviderMediaId != existingLinkForTitleProvider.ExternalId))
            {
                return MediaLinkResult.LinkInUse(existingLinkForTitleProvider.ExternalId);
            }

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
            var entryIdentityIsBeingReplaced = string.Equals(
                    entry.Provider,
                    normalizedProvider,
                    StringComparison.OrdinalIgnoreCase)
                && entry.ProviderMediaId != providerMediaId;

            if (entryIdentityIsBeingReplaced && !confirmReplacement)
            {
                return MediaLinkResult.ReplacementRequired(entry.ProviderMediaId);
            }

            if (entryIdentityIsBeingReplaced)
            {
                var hasOtherIncompatibleEntries = await _dbContext.MediaLibraryEntries
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.MediaTitleId == entry.MediaTitleId
                        && item.Provider == normalizedProvider
                        && item.ProviderMediaId != providerMediaId
                        && item.Id != entry.Id,
                        cancellationToken);

                if (hasOtherIncompatibleEntries)
                {
                    return MediaLinkResult.LinkInUse(entry.ProviderMediaId);
                }
            }

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

        // A confirmed same-provider correction must keep the initiating library entry's remote identity in sync.
        if (string.Equals(entry.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase)
            && entry.ProviderMediaId != providerMediaId)
        {
            entry.ProviderMediaId = providerMediaId;
            entry.LastMutationSource = MediaMutationSources.UserProviderIdentityCorrection;
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

        var isUsedByLibraryEntry = await _dbContext.MediaLibraryEntries
            .AsNoTracking()
            .AnyAsync(item =>
                item.MediaTitleId == entry.MediaTitleId
                && item.Provider == normalizedProvider
                && item.ProviderMediaId == link.ExternalId,
                cancellationToken);

        if (isUsedByLibraryEntry)
        {
            return MediaLinkResult.LinkInUse(link.ExternalId);
        }

        _dbContext.MediaProviderLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} unlinked MediaTitle {TitleId} from provider {Provider}.",
            userId, entry.MediaTitleId, normalizedProvider);

        return MediaLinkResult.Success();
    }
}
