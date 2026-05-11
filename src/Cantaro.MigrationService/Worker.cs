using System.Diagnostics;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

using OpenTelemetry.Trace;

namespace Cantaro.MigrationService;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger<Worker> logger) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(
        CancellationToken cancellationToken)
    {
        using var activity = s_activitySource.StartActivity(
            "Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

            await RunMigrationAsync(dbContext, cancellationToken);
            await SeedTestUserAsync(userManager, hostEnvironment, configuration, logger);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task RunMigrationAsync(
        ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Run migration in a transaction to avoid partial migration if it fails.
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }

    private static async Task SeedDataAsync(
        ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        // SupportTicket firstTicket = new()
        // {
        //     Title = "Test Ticket",
        //     Description = "Default ticket, please ignore!",
        //     Completed = true
        // };

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Seed the database
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(cancellationToken);

            // await dbContext.Tickets.AddAsync(firstTicket, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static async Task SeedTestUserAsync(
        UserManager<User> userManager,
        IHostEnvironment hostEnvironment,
        IConfiguration configuration,
        ILogger logger)
    {
        if (!hostEnvironment.IsDevelopment())
        {
            return;
        }

        var enabled = configuration.GetValue("SeedUser:Enabled", false);
        if (!enabled)
        {
            return;
        }

        var email = configuration["SeedUser:Email"]?.Trim();
        var password = configuration["SeedUser:Password"];

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("SeedUser:Email must be configured when SeedUser:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("SeedUser:Password must be configured when SeedUser:Enabled is true.");
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new User
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            EnsureIdentitySuccess(createResult, $"Creating seeded test user '{email}'");
            logger.LogInformation("Seeded test user {Email}.", email);
            return;
        }

        var needsUserUpdate = false;
        if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
        {
            user.UserName = email;
            needsUserUpdate = true;
        }

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            needsUserUpdate = true;
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            needsUserUpdate = true;
        }

        if (needsUserUpdate)
        {
            var updateResult = await userManager.UpdateAsync(user);
            EnsureIdentitySuccess(updateResult, $"Updating seeded test user '{email}'");
        }

        if (await userManager.CheckPasswordAsync(user, password))
        {
            logger.LogInformation("Seeded test user {Email} already exists.", email);
            return;
        }

        IdentityResult passwordResult;
        if (await userManager.HasPasswordAsync(user))
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            passwordResult = await userManager.ResetPasswordAsync(user, resetToken, password);
        }
        else
        {
            passwordResult = await userManager.AddPasswordAsync(user, password);
        }

        EnsureIdentitySuccess(passwordResult, $"Setting password for seeded test user '{email}'");
        logger.LogInformation("Reset seeded test user password for {Email}.", email);
    }

    private static void EnsureIdentitySuccess(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation} failed: {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }
}