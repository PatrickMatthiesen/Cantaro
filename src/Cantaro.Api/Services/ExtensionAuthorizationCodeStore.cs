using System.Security.Cryptography;
using System.Text;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

public sealed class ExtensionAuthorizationCodeStore
{
    private readonly ApplicationDbContext _dbContext;

    public ExtensionAuthorizationCodeStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task StoreAsync(string code, ExtensionAuthorizationCodeEntry entry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var utcNow = DateTime.UtcNow;
        await CleanupExpiredCodesAsync(utcNow, cancellationToken);

        _dbContext.ExtensionAuthorizationCodes.Add(new ExtensionAuthorizationCode
        {
            UserId = entry.UserId,
            ClientId = entry.ClientId,
            RedirectUri = entry.RedirectUri,
            CodeChallenge = entry.CodeChallenge,
            CodeHash = HashAuthorizationCode(code.Trim()),
            CreatedAt = utcNow,
            ExpiresAt = entry.ExpiresAtUtc
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExtensionAuthorizationCodeEntry?> TryRedeemAsync(
        string code,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        await CleanupExpiredCodesAsync(utcNow, cancellationToken);

        var authorizationCode = await _dbContext.ExtensionAuthorizationCodes
            .SingleOrDefaultAsync(entry => entry.CodeHash == HashAuthorizationCode(code.Trim()), cancellationToken);

        if (authorizationCode is null)
        {
            return null;
        }

        _dbContext.ExtensionAuthorizationCodes.Remove(authorizationCode);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        if (authorizationCode.ExpiresAt <= utcNow)
        {
            return null;
        }

        return new ExtensionAuthorizationCodeEntry
        {
            UserId = authorizationCode.UserId,
            ClientId = authorizationCode.ClientId,
            RedirectUri = authorizationCode.RedirectUri,
            CodeChallenge = authorizationCode.CodeChallenge,
            ExpiresAtUtc = authorizationCode.ExpiresAt
        };
    }

    private Task CleanupExpiredCodesAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        return _dbContext.ExtensionAuthorizationCodes
            .Where(entry => entry.ExpiresAt <= utcNow)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string HashAuthorizationCode(string code)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return WebEncoders.Base64UrlEncode(hashBytes);
    }
}

public sealed class ExtensionAuthorizationCodeEntry
{
    public required int UserId { get; init; }

    public required string ClientId { get; init; }

    public required string RedirectUri { get; init; }

    public required string CodeChallenge { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }
}