using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cantaro.Api.Tests;

internal sealed class SpotifyTestServiceScopeFactory(
    DbContextOptions<ApplicationDbContext> dbContextOptions)
    : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new Scope(new ApplicationDbContext(dbContextOptions));

    private sealed class Scope(ApplicationDbContext dbContext) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new Provider(dbContext);

        public void Dispose() => dbContext.Dispose();

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Provider(ApplicationDbContext dbContext) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ApplicationDbContext) ? dbContext : null;
    }
}
