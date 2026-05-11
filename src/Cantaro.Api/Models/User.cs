using Microsoft.AspNetCore.Identity;

namespace Cantaro.Api.Models;

public class User : IdentityUser<int>
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Navigation property for connected service accounts
    /// </summary>
    public ICollection<ConnectedServiceAccount> ConnectedServiceAccounts { get; set; } = [];

    public ICollection<MediaLibraryEntry> MediaLibraryEntries { get; set; } = [];

    public ICollection<MediaProviderOperation> MediaProviderOperations { get; set; } = [];
}
