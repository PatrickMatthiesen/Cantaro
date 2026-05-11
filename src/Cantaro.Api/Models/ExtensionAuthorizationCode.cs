namespace Cantaro.Api.Models;

public class ExtensionAuthorizationCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public required string ClientId { get; set; }

    public required string RedirectUri { get; set; }

    public required string CodeChallenge { get; set; }

    public required string CodeHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public User? User { get; set; }
}