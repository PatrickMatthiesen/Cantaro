using Cantaro.Api.Models;

namespace Cantaro.Api.Tests;

internal static class TestUserFactory
{
    public static User Create(int id, string email)
    {
        return new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
