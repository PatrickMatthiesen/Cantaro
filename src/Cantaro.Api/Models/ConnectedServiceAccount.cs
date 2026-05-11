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
    /// OAuth scopes granted
    /// </summary>
    public string? Scopes { get; set; }
    
    /// <summary>
    /// When the access token expires (if known)
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }
    
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
}
