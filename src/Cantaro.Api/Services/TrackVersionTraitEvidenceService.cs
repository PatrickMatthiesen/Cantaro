using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed record AssertTrackVersionTraitCommand(
    Guid TrackId,
    string TraitKey,
    decimal Confidence,
    string EvidenceSource,
    string EvidenceIdentity,
    string EvidenceMethod,
    string? MethodVersion,
    string AssertedByType,
    string AssertedById);

public sealed record RevokeTrackVersionTraitCommand(
    string RevokedByType,
    string RevokedById,
    string Reason);

public sealed record TrackVersionTraitEvidenceDto(
    Guid Id,
    Guid TrackId,
    string TraitKey,
    decimal Confidence,
    string EvidenceSource,
    string EvidenceIdentity,
    string EvidenceMethod,
    string? MethodVersion,
    string AssertedByType,
    string AssertedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    string? RevokedByType,
    string? RevokedById,
    string? RevocationReason,
    Guid? SupersedesTraitId,
    bool IsActive,
    bool HasPotentialConflict);

public interface ITrackVersionTraitEvidenceService
{
    Task<TrackVersionTraitEvidenceDto> AssertAsync(
        AssertTrackVersionTraitCommand command,
        CancellationToken cancellationToken = default);

    Task<TrackVersionTraitEvidenceDto?> RevokeAsync(
        Guid traitId,
        RevokeTrackVersionTraitCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackVersionTraitEvidenceDto>> GetActiveAsync(
        Guid trackId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackVersionTraitEvidenceDto>> GetHistoryAsync(
        Guid trackId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes and reads version-trait evidence without changing Song membership or
/// provider presentations. Assertions are immutable; changed evidence revokes
/// the old row and inserts a superseding row in one transaction.
/// </summary>
public sealed class TrackVersionTraitEvidenceService(
    ApplicationDbContext dbContext,
    ITrackVersionTraitVocabulary vocabulary,
    TimeProvider timeProvider) : ITrackVersionTraitEvidenceService
{
    public async Task<TrackVersionTraitEvidenceDto> AssertAsync(
        AssertTrackVersionTraitCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(command);
        if (normalized.Confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                command.Confidence,
                "Confidence must be between 0 and 1.");
        }

        if (!await dbContext.Tracks.AnyAsync(track => track.Id == normalized.TrackId, cancellationToken))
        {
            throw new KeyNotFoundException($"Track '{normalized.TrackId}' was not found.");
        }

        var active = await FindActiveAsync(normalized, cancellationToken);
        if (active is not null && HasSameMeaning(active, normalized))
        {
            return ToDto(active, false);
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (active is not null)
            {
                var transitioned = await dbContext.TrackVersionTraits
                    .Where(trait => trait.Id == active.Id && trait.RevokedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(trait => trait.RevokedAt, now)
                            .SetProperty(trait => trait.RevokedByType, normalized.AssertedByType)
                            .SetProperty(trait => trait.RevokedById, normalized.AssertedById)
                            .SetProperty(
                                trait => trait.RevocationReason,
                                "Superseded by updated evidence."),
                        cancellationToken);
                if (transitioned != 1)
                {
                    throw new DbUpdateConcurrencyException(
                        "The active trait assertion changed before it could be superseded.");
                }
            }

            var assertion = new TrackVersionTrait
            {
                Id = Guid.NewGuid(),
                TrackId = normalized.TrackId,
                TraitKey = normalized.TraitKey,
                Confidence = normalized.Confidence,
                EvidenceSource = normalized.EvidenceSource,
                EvidenceIdentity = normalized.EvidenceIdentity,
                EvidenceMethod = normalized.EvidenceMethod,
                MethodVersion = normalized.MethodVersion,
                AssertedByType = normalized.AssertedByType,
                AssertedById = normalized.AssertedById,
                CreatedAt = now,
                SupersedesTraitId = active?.Id
            };
            dbContext.TrackVersionTraits.Add(assertion);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDto(assertion, false);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var winner = await FindActiveAsync(normalized, cancellationToken);
            if (winner is not null && HasSameMeaning(winner, normalized))
            {
                return ToDto(winner, false);
            }

            throw;
        }
    }

    public async Task<TrackVersionTraitEvidenceDto?> RevokeAsync(
        Guid traitId,
        RevokeTrackVersionTraitCommand command,
        CancellationToken cancellationToken = default)
    {
        var revokedByType = Required(command.RevokedByType, nameof(command.RevokedByType), 24, true);
        var revokedById = Required(command.RevokedById, nameof(command.RevokedById), 128);
        var reason = Required(command.Reason, nameof(command.Reason), 256);
        var assertion = await dbContext.TrackVersionTraits.AsNoTracking()
            .SingleOrDefaultAsync(trait => trait.Id == traitId, cancellationToken);
        if (assertion is null)
        {
            return null;
        }

        if (assertion.RevokedAt is null)
        {
            var revokedAt = timeProvider.GetUtcNow();
            await dbContext.TrackVersionTraits
                .Where(trait => trait.Id == traitId && trait.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(trait => trait.RevokedAt, revokedAt)
                        .SetProperty(trait => trait.RevokedByType, revokedByType)
                        .SetProperty(trait => trait.RevokedById, revokedById)
                        .SetProperty(trait => trait.RevocationReason, reason),
                    cancellationToken);

            assertion = await dbContext.TrackVersionTraits.AsNoTracking()
                .SingleAsync(trait => trait.Id == traitId, cancellationToken);
        }

        return ToDto(assertion, false);
    }

    public async Task<IReadOnlyList<TrackVersionTraitEvidenceDto>> GetActiveAsync(
        Guid trackId,
        CancellationToken cancellationToken = default)
    {
        var assertions = await dbContext.TrackVersionTraits.AsNoTracking()
            .Where(trait => trait.TrackId == trackId && trait.RevokedAt == null)
            .ToListAsync(cancellationToken);
        return MapWithConflicts(assertions
            .OrderBy(trait => trait.TraitKey, StringComparer.Ordinal)
            .ThenBy(trait => trait.CreatedAt)
            .ThenBy(trait => trait.Id)
            .ToArray());
    }

    public async Task<IReadOnlyList<TrackVersionTraitEvidenceDto>> GetHistoryAsync(
        Guid trackId,
        CancellationToken cancellationToken = default)
    {
        var assertions = await dbContext.TrackVersionTraits.AsNoTracking()
            .Where(trait => trait.TrackId == trackId)
            .ToListAsync(cancellationToken);
        return MapWithConflicts(assertions
            .OrderByDescending(trait => trait.CreatedAt)
            .ThenByDescending(trait => trait.Id)
            .ToArray());
    }

    private Task<TrackVersionTrait?> FindActiveAsync(
        AssertTrackVersionTraitCommand command,
        CancellationToken cancellationToken) =>
        dbContext.TrackVersionTraits.AsNoTracking().SingleOrDefaultAsync(
            trait => trait.TrackId == command.TrackId
                && trait.TraitKey == command.TraitKey
                && trait.EvidenceSource == command.EvidenceSource
                && trait.EvidenceIdentity == command.EvidenceIdentity
                && trait.EvidenceMethod == command.EvidenceMethod
                && trait.RevokedAt == null,
            cancellationToken);

    private AssertTrackVersionTraitCommand Normalize(AssertTrackVersionTraitCommand command) =>
        command with
        {
            TraitKey = vocabulary.Normalize(command.TraitKey),
            EvidenceSource = Required(command.EvidenceSource, nameof(command.EvidenceSource), 64, true),
            EvidenceIdentity = Required(command.EvidenceIdentity, nameof(command.EvidenceIdentity), 256),
            EvidenceMethod = Required(command.EvidenceMethod, nameof(command.EvidenceMethod), 96, true),
            MethodVersion = Optional(command.MethodVersion, nameof(command.MethodVersion), 64),
            AssertedByType = Required(command.AssertedByType, nameof(command.AssertedByType), 24, true),
            AssertedById = Required(command.AssertedById, nameof(command.AssertedById), 128)
        };

    private IReadOnlyList<TrackVersionTraitEvidenceDto> MapWithConflicts(
        IReadOnlyCollection<TrackVersionTrait> assertions)
    {
        var activeKeys = assertions
            .Where(assertion => assertion.RevokedAt == null)
            .Select(assertion => assertion.TraitKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return assertions.Select(assertion =>
        {
            var hasConflict = assertion.RevokedAt == null
                && activeKeys.Any(other => vocabulary.ArePotentiallyConflicting(assertion.TraitKey, other));
            return ToDto(assertion, hasConflict);
        }).ToArray();
    }

    private static bool HasSameMeaning(
        TrackVersionTrait assertion,
        AssertTrackVersionTraitCommand command) =>
        assertion.Confidence == command.Confidence
        && string.Equals(assertion.MethodVersion, command.MethodVersion, StringComparison.Ordinal);

    private static TrackVersionTraitEvidenceDto ToDto(
        TrackVersionTrait assertion,
        bool hasPotentialConflict) => new(
        assertion.Id,
        assertion.TrackId,
        assertion.TraitKey,
        assertion.Confidence,
        assertion.EvidenceSource,
        assertion.EvidenceIdentity,
        assertion.EvidenceMethod,
        assertion.MethodVersion,
        assertion.AssertedByType,
        assertion.AssertedById,
        assertion.CreatedAt,
        assertion.RevokedAt,
        assertion.RevokedByType,
        assertion.RevokedById,
        assertion.RevocationReason,
        assertion.SupersedesTraitId,
        assertion.RevokedAt == null,
        hasPotentialConflict);

    private static string Required(string value, string name, int maxLength, bool lowerCase = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = lowerCase ? value.Trim().ToLowerInvariant() : value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? Optional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }
}
