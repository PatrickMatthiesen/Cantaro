namespace Cantaro.Api.Models;

/// <summary>
/// Represents a queued provider mutation that Cantaro owns and retries on the
/// backend.
/// </summary>
public class MediaProviderOperation
{
    public Guid Id { get; set; }

    public Guid MediaLibraryProviderBindingId { get; set; }

    public Guid? BatchId { get; set; }

    public required string OperationType { get; set; }

    public required string PayloadJson { get; set; }

    public required string Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public MediaLibraryProviderBinding? MediaLibraryProviderBinding { get; set; }
}
