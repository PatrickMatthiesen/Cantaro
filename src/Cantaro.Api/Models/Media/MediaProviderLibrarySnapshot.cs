namespace Cantaro.Api.Models;

/// <summary>
/// Account-owned provider library baseline for merging incremental imports.
/// Episode identities remain intact even when Cantaro displays aggregate progress.
/// </summary>
public sealed class MediaProviderLibrarySnapshot
{
    public int ConnectedServiceAccountId { get; set; }
    public string? Cursor { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
    public ConnectedServiceAccount? ConnectedServiceAccount { get; set; }
}
