using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class ApplicationDbContextTests
{
    [Fact]
    public async Task SaveChangesUpdatesUserUpdatedAtWhenUserIsModified()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var createdAt = DateTime.UtcNow.AddDays(-2);
        var originalUpdatedAt = DateTime.UtcNow.AddDays(-1);

        await using (var seedContext = new ApplicationDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            seedContext.Users.Add(new User
            {
                UserName = "listener@example.com",
                Email = "listener@example.com",
                CreatedAt = createdAt,
                UpdatedAt = originalUpdatedAt
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var updateContext = new ApplicationDbContext(options))
        {
            var user = await updateContext.Users.SingleAsync();
            user.Email = "listener.updated@example.com";

            await updateContext.SaveChangesAsync();
        }

        await using var assertContext = new ApplicationDbContext(options);
        var updatedUser = await assertContext.Users.SingleAsync();
        Assert.Equal(createdAt, updatedUser.CreatedAt);
        Assert.True(updatedUser.UpdatedAt > originalUpdatedAt);
    }
}
