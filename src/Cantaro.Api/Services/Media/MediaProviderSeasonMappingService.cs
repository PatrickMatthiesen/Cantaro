using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class MediaProviderSeasonMappingService(
    ApplicationDbContext dbContext,
    ILogger<MediaProviderSeasonMappingService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly ILogger<MediaProviderSeasonMappingService> _logger = logger;

    public async Task<MediaProviderSeasonMapping?> FindAsync(
        string provider,
        string? providerSeriesId,
        string? providerSeasonId,
        int? providerSeasonNumber,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var normalizedSeriesId = NormalizeIdentifier(providerSeriesId);
        var normalizedSeasonId = NormalizeIdentifier(providerSeasonId);
        if (normalizedSeriesId is null || (normalizedSeasonId is null && providerSeasonNumber is not > 0))
        {
            return null;
        }

        var query = _dbContext.MediaProviderSeasonMappings
            .Include(mapping => mapping.MediaTitle)
            .Where(mapping => mapping.Provider == normalizedProvider
                && mapping.ProviderSeriesId == normalizedSeriesId
                && !mapping.HasConflict);

        if (normalizedSeasonId is not null)
        {
            return await query.FirstOrDefaultAsync(
                mapping => mapping.ProviderSeasonId == normalizedSeasonId,
                cancellationToken);
        }

        return providerSeasonNumber is > 0
            ? await query.FirstOrDefaultAsync(
                mapping => mapping.ProviderSeasonId == null
                    && mapping.ProviderSeasonNumber == providerSeasonNumber,
                cancellationToken)
            : null;
    }

    public async Task<MediaProviderSeasonMapping?> EstablishAsync(
        string provider,
        string? providerSeriesId,
        string? providerSeasonId,
        int? providerSeasonNumber,
        Guid mediaTitleId,
        int episodeOffset,
        string mappingSource,
        decimal confidence,
        CancellationToken cancellationToken)
    {
        if (mappingSource is not MediaProviderSeasonMappingSources.ProviderEpisodeIdentity
            and not MediaProviderSeasonMappingSources.Manual)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mappingSource),
                mappingSource,
                "Only independently exact episode identities or explicit manual resolutions may establish a provider season mapping.");
        }

        var normalizedProvider = NormalizeProvider(provider);
        var normalizedSeriesId = NormalizeIdentifier(providerSeriesId);
        var normalizedSeasonId = NormalizeIdentifier(providerSeasonId);
        if (normalizedSeriesId is null || (normalizedSeasonId is null && providerSeasonNumber is not > 0))
        {
            return null;
        }

        var existing = await _dbContext.MediaProviderSeasonMappings
            .FirstOrDefaultAsync(mapping => mapping.Provider == normalizedProvider
                && mapping.ProviderSeriesId == normalizedSeriesId
                && ((normalizedSeasonId != null && mapping.ProviderSeasonId == normalizedSeasonId)
                    || (normalizedSeasonId == null
                        && mapping.ProviderSeasonId == null
                        && mapping.ProviderSeasonNumber == providerSeasonNumber)),
                cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            existing = new MediaProviderSeasonMapping
            {
                Id = Guid.NewGuid(),
                Provider = normalizedProvider,
                ProviderSeriesId = normalizedSeriesId,
                ProviderSeasonId = normalizedSeasonId,
                ProviderSeasonNumber = providerSeasonNumber,
                MediaTitleId = mediaTitleId,
                EpisodeOffset = episodeOffset,
                MappingSource = mappingSource,
                Confidence = confidence,
                FirstSeenAt = now,
                LastVerifiedAt = now
            };
            _dbContext.MediaProviderSeasonMappings.Add(existing);
            return existing;
        }

        existing.LastVerifiedAt = now;
        existing.ProviderSeasonNumber ??= providerSeasonNumber;
        if (existing.MediaTitleId != mediaTitleId || existing.EpisodeOffset != episodeOffset)
        {
            if (mappingSource == MediaProviderSeasonMappingSources.Manual)
            {
                existing.MediaTitleId = mediaTitleId;
                existing.EpisodeOffset = episodeOffset;
                existing.MappingSource = mappingSource;
                existing.Confidence = confidence;
                existing.HasConflict = false;
                return existing;
            }

            existing.HasConflict = true;
            _logger.LogWarning(
                "Provider season mapping conflict for {Provider}/{ProviderSeriesId}/{ProviderSeasonId}: existing title {ExistingMediaTitleId} offset {ExistingOffset}, observed title {MediaTitleId} offset {EpisodeOffset}.",
                normalizedProvider,
                normalizedSeriesId,
                normalizedSeasonId ?? providerSeasonNumber?.ToString(),
                existing.MediaTitleId,
                existing.EpisodeOffset,
                mediaTitleId,
                episodeOffset);
        }
        else
        {
            existing.MappingSource = mappingSource;
            existing.Confidence = Math.Max(existing.Confidence, confidence);
            existing.HasConflict = false;
        }

        return existing;
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant();

    private static string? NormalizeIdentifier(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
