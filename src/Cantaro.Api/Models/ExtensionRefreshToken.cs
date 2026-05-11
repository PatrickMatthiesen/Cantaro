namespace Cantaro.Api.Models;

public class ExtensionRefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }

    public required string ClientId { get; set; }

    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public User? User { get; set; }
}