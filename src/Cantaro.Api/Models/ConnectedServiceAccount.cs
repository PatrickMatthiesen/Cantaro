namespace Cantaro.Api.Models;

/// <summary>
/// Represents a connected third-party service account (e.g., YouTube, Spotify)
/// </summary>
public class ConnectedServiceAccount
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to the User
    /// </summary>
    public int UserId { get; set; }
    
    /// <summary>
    /// The service provider (e.g., "youtube", "spotify")
    /// </summary>
    public required string Service { get; set; }
    
    /// <summary>
    /// The external account ID from the service
    /// </summary>
    public required string ExternalAccountId { get; set; }
    
    /// <summary>
    /// Display name or email from the service
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Encrypted refresh token for background sync
    /// </summary>
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>
    /// Encrypted access token retained until shortly before its provider expiry.
    /// </summary>
    public string? EncryptedAccessToken { get; set; }
    
    /// <summary>
    /// OAuth scopes granted
    /// </summary>
    public string? Scopes { get; set; }
    
    /// <summary>
    /// When the access token expires (if known)
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// Absolute provider refresh-token expiry, when the provider publishes one.
    /// </summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>
    /// Fencing version incremented whenever provider tokens are replaced or invalidated.
    /// </summary>
    public long TokenVersion { get; set; }

    /// <summary>
    /// Opaque ownership token for the current provider refresh attempt. This is
    /// not a machine identifier; it fences stale workers while the expiry changes.
    /// </summary>
    public Guid? TokenRefreshLeaseId { get; set; }

    /// <summary>
    /// Short lease expiry that allows another process to recover a refresh abandoned by a crashed owner.
    /// </summary>
    public DateTime? TokenRefreshLeaseExpiresAt { get; set; }

    /// <summary>
    /// Provider-neutral connection state: connected or reconnect_required.
    /// </summary>
    public string ConnectionState { get; set; } = "connected";

    public DateTime? ReconnectRequiredAt { get; set; }

    /// <summary>
    /// Safe machine-readable reason. Raw provider responses and credentials are never stored here.
    /// </summary>
    public string? ReconnectReason { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Navigation property to User
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Media library entries that are currently attached to this live account.
    /// Historical rows can outlive the account and will then have a null
    /// ConnectedServiceAccountId.
    /// </summary>
    public ICollection<MediaLibraryEntry> MediaLibraryEntries { get; set; } = [];

    public ICollection<MediaProviderOperation> MediaProviderOperations { get; set; } = [];

    public ICollection<ServicePlaylistMapping> ServicePlaylistMappings { get; set; } = [];
}
