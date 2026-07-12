using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public enum TrackSourceClassificationPatchOperation
{
    Unchanged,
    Set,
    Clear
}

public sealed record TrackSourceClassificationProposal(
    string Value,
    decimal Confidence,
    string EvidenceSource,
    string EvidenceIdentity,
    string EvidenceMethod,
    string? MethodVersion);

public sealed record TrackSourceClassificationToken(Guid Revision);

public sealed record TrackSourceClassificationChange(
    TrackSourceClassificationPatchOperation Operation,
    TrackSourceClassificationProposal? Proposal,
    TrackSourceClassificationToken? Expected)
{
    public static TrackSourceClassificationChange Unchanged() => new(
        TrackSourceClassificationPatchOperation.Unchanged,
        null,
        null);

    public static TrackSourceClassificationChange Set(
        TrackSourceClassificationProposal proposal,
        TrackSourceClassificationToken? expected = null) => new(
        TrackSourceClassificationPatchOperation.Set,
        proposal,
        expected);

    public static TrackSourceClassificationChange Clear(
        TrackSourceClassificationToken? expected = null) => new(
        TrackSourceClassificationPatchOperation.Clear,
        null,
        expected);
}

public sealed record TrackSourcePresentationPatch(
    TrackSourceClassificationChange PresentationKind,
    TrackSourceClassificationChange UploaderAuthority);

public sealed record TrackSourceClassificationDto(
    string Value,
    decimal Confidence,
    string EvidenceSource,
    string EvidenceIdentity,
    string EvidenceMethod,
    string? MethodVersion,
    DateTimeOffset ClassifiedAt,
    Guid Revision)
{
    public TrackSourceClassificationToken Token => new(Revision);
}

public sealed record TrackSourcePresentationDto(
    Guid TrackSourceId,
    TrackSourceClassificationDto? PresentationKind,
    TrackSourceClassificationDto? UploaderAuthority);

public interface ITrackSourcePresentationClassificationService
{
    Task<TrackSourcePresentationDto?> GetAsync(
        Guid trackSourceId,
        CancellationToken cancellationToken = default);

    Task<TrackSourcePresentationDto> ApplyAsync(
        Guid trackSourceId,
        TrackSourcePresentationPatch patch,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Maintains current provider-presentation classifications on TrackSourceId.
/// Each dimension is independently patched and guarded by an optimistic token.
/// This service never changes Track, Song, trait, playlist, or sync state.
/// </summary>
public sealed class TrackSourcePresentationClassificationService(
    ApplicationDbContext dbContext,
    ITrackSourcePresentationVocabulary vocabulary,
    TimeProvider timeProvider) : ITrackSourcePresentationClassificationService
{
    public async Task<TrackSourcePresentationDto?> GetAsync(
        Guid trackSourceId,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.TrackSourceIds.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == trackSourceId, cancellationToken);
        return source is null ? null : ToDto(source);
    }

    public async Task<TrackSourcePresentationDto> ApplyAsync(
        Guid trackSourceId,
        TrackSourcePresentationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var source = await dbContext.TrackSourceIds.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == trackSourceId, cancellationToken)
            ?? throw new KeyNotFoundException($"TrackSourceId '{trackSourceId}' was not found.");
        var current = ToDto(source);
        var kindChange = NormalizeKindChange(patch.PresentationKind);
        var authorityChange = NormalizeAuthorityChange(patch.UploaderAuthority);
        var changeKind = ShouldChange(current.PresentationKind, kindChange);
        var changeAuthority = ShouldChange(current.UploaderAuthority, authorityChange);
        if (!changeKind && !changeAuthority)
        {
            return current;
        }

        var classifiedAt = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (changeKind)
        {
            ValidateExpected(current.PresentationKind, kindChange.Expected, "presentation kind");
            var affected = await UpdateKindAsync(
                trackSourceId,
                current.PresentationKind,
                kindChange,
                classifiedAt,
                cancellationToken);
            if (affected != 1)
            {
                throw new DbUpdateConcurrencyException("The presentation kind changed before the patch was applied.");
            }
        }

        if (changeAuthority)
        {
            ValidateExpected(current.UploaderAuthority, authorityChange.Expected, "uploader authority");
            var affected = await UpdateAuthorityAsync(
                trackSourceId,
                current.UploaderAuthority,
                authorityChange,
                classifiedAt,
                cancellationToken);
            if (affected != 1)
            {
                throw new DbUpdateConcurrencyException("The uploader authority changed before the patch was applied.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return (await GetAsync(trackSourceId, cancellationToken))!;
    }

    private async Task<int> UpdateKindAsync(
        Guid id,
        TrackSourceClassificationDto? current,
        TrackSourceClassificationChange change,
        DateTimeOffset classifiedAt,
        CancellationToken cancellationToken)
    {
        var query = GuardKind(id, current);
        if (change.Operation == TrackSourceClassificationPatchOperation.Clear)
        {
            return await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.PresentationKind, (string?)null)
                    .SetProperty(item => item.PresentationKindConfidence, (decimal?)null)
                    .SetProperty(item => item.PresentationKindEvidenceSource, (string?)null)
                    .SetProperty(item => item.PresentationKindEvidenceIdentity, (string?)null)
                    .SetProperty(item => item.PresentationKindEvidenceMethod, (string?)null)
                    .SetProperty(item => item.PresentationKindMethodVersion, (string?)null)
                    .SetProperty(item => item.PresentationKindClassifiedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.PresentationKindRevision, (Guid?)null),
                cancellationToken);
        }

        var proposal = change.Proposal!;
        var revision = Guid.NewGuid();
        return await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(item => item.PresentationKind, proposal.Value)
                .SetProperty(item => item.PresentationKindConfidence, proposal.Confidence)
                .SetProperty(item => item.PresentationKindEvidenceSource, proposal.EvidenceSource)
                .SetProperty(item => item.PresentationKindEvidenceIdentity, proposal.EvidenceIdentity)
                .SetProperty(item => item.PresentationKindEvidenceMethod, proposal.EvidenceMethod)
                .SetProperty(item => item.PresentationKindMethodVersion, proposal.MethodVersion)
                .SetProperty(item => item.PresentationKindClassifiedAt, classifiedAt)
                .SetProperty(item => item.PresentationKindRevision, revision),
            cancellationToken);
    }

    private async Task<int> UpdateAuthorityAsync(
        Guid id,
        TrackSourceClassificationDto? current,
        TrackSourceClassificationChange change,
        DateTimeOffset classifiedAt,
        CancellationToken cancellationToken)
    {
        var query = GuardAuthority(id, current);
        if (change.Operation == TrackSourceClassificationPatchOperation.Clear)
        {
            return await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.UploaderAuthority, (string?)null)
                    .SetProperty(item => item.UploaderAuthorityConfidence, (decimal?)null)
                    .SetProperty(item => item.UploaderAuthorityEvidenceSource, (string?)null)
                    .SetProperty(item => item.UploaderAuthorityEvidenceIdentity, (string?)null)
                    .SetProperty(item => item.UploaderAuthorityEvidenceMethod, (string?)null)
                    .SetProperty(item => item.UploaderAuthorityMethodVersion, (string?)null)
                    .SetProperty(item => item.UploaderAuthorityClassifiedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.UploaderAuthorityRevision, (Guid?)null),
                cancellationToken);
        }

        var proposal = change.Proposal!;
        var revision = Guid.NewGuid();
        return await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(item => item.UploaderAuthority, proposal.Value)
                .SetProperty(item => item.UploaderAuthorityConfidence, proposal.Confidence)
                .SetProperty(item => item.UploaderAuthorityEvidenceSource, proposal.EvidenceSource)
                .SetProperty(item => item.UploaderAuthorityEvidenceIdentity, proposal.EvidenceIdentity)
                .SetProperty(item => item.UploaderAuthorityEvidenceMethod, proposal.EvidenceMethod)
                .SetProperty(item => item.UploaderAuthorityMethodVersion, proposal.MethodVersion)
                .SetProperty(item => item.UploaderAuthorityClassifiedAt, classifiedAt)
                .SetProperty(item => item.UploaderAuthorityRevision, revision),
            cancellationToken);
    }

    private IQueryable<Models.TrackSourceId> GuardKind(
        Guid id,
        TrackSourceClassificationDto? current) => current is null
        ? dbContext.TrackSourceIds.Where(item => item.Id == id && item.PresentationKind == null)
        : dbContext.TrackSourceIds.Where(item => item.Id == id
            && item.PresentationKindRevision == current.Revision);

    private IQueryable<Models.TrackSourceId> GuardAuthority(
        Guid id,
        TrackSourceClassificationDto? current) => current is null
        ? dbContext.TrackSourceIds.Where(item => item.Id == id && item.UploaderAuthority == null)
        : dbContext.TrackSourceIds.Where(item => item.Id == id
            && item.UploaderAuthorityRevision == current.Revision);

    private TrackSourceClassificationChange NormalizeKindChange(TrackSourceClassificationChange change) =>
        NormalizeChange(change, vocabulary.NormalizePresentationKind);

    private TrackSourceClassificationChange NormalizeAuthorityChange(TrackSourceClassificationChange change) =>
        NormalizeChange(change, vocabulary.NormalizeUploaderAuthority);

    private static TrackSourceClassificationChange NormalizeChange(
        TrackSourceClassificationChange change,
        Func<string, string> normalizeValue)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Operation != TrackSourceClassificationPatchOperation.Set)
        {
            if (change.Proposal is not null)
            {
                throw new ArgumentException("Only a Set patch may include a proposal.", nameof(change));
            }

            return change;
        }

        var proposal = change.Proposal
            ?? throw new ArgumentException("A Set patch requires a proposal.", nameof(change));
        if (proposal.Confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(change), "Confidence must be between 0 and 1.");
        }

        return change with
        {
            Proposal = proposal with
            {
                Value = normalizeValue(proposal.Value),
                EvidenceSource = Required(proposal.EvidenceSource, nameof(proposal.EvidenceSource), 64, true),
                EvidenceIdentity = Required(proposal.EvidenceIdentity, nameof(proposal.EvidenceIdentity), 256),
                EvidenceMethod = Required(proposal.EvidenceMethod, nameof(proposal.EvidenceMethod), 96, true),
                MethodVersion = Optional(proposal.MethodVersion, nameof(proposal.MethodVersion), 64)
            }
        };
    }

    private static bool ShouldChange(
        TrackSourceClassificationDto? current,
        TrackSourceClassificationChange change) => change.Operation switch
        {
            TrackSourceClassificationPatchOperation.Unchanged => false,
            TrackSourceClassificationPatchOperation.Clear => current is not null,
            TrackSourceClassificationPatchOperation.Set => !HasSameMeaning(current, change.Proposal!),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

    private static bool HasSameMeaning(
        TrackSourceClassificationDto? current,
        TrackSourceClassificationProposal proposal) => current is not null
        && current.Value == proposal.Value
        && current.Confidence == proposal.Confidence
        && current.EvidenceSource == proposal.EvidenceSource
        && current.EvidenceIdentity == proposal.EvidenceIdentity
        && current.EvidenceMethod == proposal.EvidenceMethod
        && current.MethodVersion == proposal.MethodVersion;

    private static void ValidateExpected(
        TrackSourceClassificationDto? current,
        TrackSourceClassificationToken? expected,
        string dimension)
    {
        if (current is null && expected is null)
        {
            return;
        }

        if (current is null
            || expected is null
            || current.Revision != expected.Revision)
        {
            throw new DbUpdateConcurrencyException($"The expected {dimension} classification is stale.");
        }
    }

    private static TrackSourcePresentationDto ToDto(Models.TrackSourceId source) => new(
        source.Id,
        Classification(
            source.PresentationKind,
            source.PresentationKindConfidence,
            source.PresentationKindEvidenceSource,
            source.PresentationKindEvidenceIdentity,
            source.PresentationKindEvidenceMethod,
            source.PresentationKindMethodVersion,
            source.PresentationKindClassifiedAt,
            source.PresentationKindRevision),
        Classification(
            source.UploaderAuthority,
            source.UploaderAuthorityConfidence,
            source.UploaderAuthorityEvidenceSource,
            source.UploaderAuthorityEvidenceIdentity,
            source.UploaderAuthorityEvidenceMethod,
            source.UploaderAuthorityMethodVersion,
            source.UploaderAuthorityClassifiedAt,
            source.UploaderAuthorityRevision));

    private static TrackSourceClassificationDto? Classification(
        string? value,
        decimal? confidence,
        string? evidenceSource,
        string? evidenceIdentity,
        string? evidenceMethod,
        string? methodVersion,
        DateTimeOffset? classifiedAt,
        Guid? revision) => value is null ? null : new TrackSourceClassificationDto(
        value,
        confidence!.Value,
        evidenceSource!,
        evidenceIdentity!,
        evidenceMethod!,
        methodVersion,
        classifiedAt!.Value,
        revision!.Value);

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
