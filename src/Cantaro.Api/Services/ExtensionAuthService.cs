using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cantaro.Api.Services;

public sealed class ExtensionAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ExtensionAuthorizationCodeStore _authorizationCodeStore;
    private readonly ExtensionAuthOptions _options;
    private readonly IWebHostEnvironment _environment;

    public ExtensionAuthService(
        ApplicationDbContext dbContext,
        ExtensionAuthorizationCodeStore authorizationCodeStore,
        IOptions<ExtensionAuthOptions> options,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _authorizationCodeStore = authorizationCodeStore;
        _options = options.Value;
        _environment = environment;
    }

    public async Task<string> CreateAuthorizationCodeAsync(
        User user,
        string clientId,
        string redirectUri,
        string codeChallenge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var utcNow = DateTime.UtcNow;

        await _authorizationCodeStore.StoreAsync(code, new ExtensionAuthorizationCodeEntry
        {
            UserId = user.Id,
            ClientId = clientId.Trim(),
            RedirectUri = redirectUri.Trim(),
            CodeChallenge = codeChallenge.Trim(),
            ExpiresAtUtc = utcNow.AddMinutes(_options.AuthorizationCodeLifetimeMinutes)
        }, cancellationToken);

        return code;
    }

    public async Task<ExtensionTokenResponse> ExchangeAuthorizationCodeAsync(
        string code,
        string clientId,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        var entry = await _authorizationCodeStore.TryRedeemAsync(code.Trim(), DateTime.UtcNow, cancellationToken);

        if (entry is null)
        {
            throw new InvalidOperationException("The authorization code is invalid or has expired.");
        }

        if (!string.Equals(entry.ClientId, clientId.Trim(), StringComparison.Ordinal)
            || !string.Equals(entry.RedirectUri, redirectUri.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The authorization request no longer matches the original redirect or client.");
        }

        if (!ValidatePkce(codeVerifier, entry.CodeChallenge))
        {
            throw new InvalidOperationException("The PKCE verifier did not match the original challenge.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == entry.UserId, cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException("The user for this authorization code no longer exists.");
        }

        return await IssueTokenSetAsync(user, clientId.Trim(), cancellationToken);
    }

    public async Task<ExtensionTokenResponse> RefreshAccessTokenAsync(
        string refreshToken,
        string clientId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var utcNow = DateTime.UtcNow;
        var tokenHash = HashToken(refreshToken.Trim());
        var refreshTokenEntity = await _dbContext.ExtensionRefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (refreshTokenEntity is null
            || refreshTokenEntity.RevokedAt.HasValue
            || refreshTokenEntity.ExpiresAt <= utcNow
            || !string.Equals(refreshTokenEntity.ClientId, clientId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The refresh token is invalid, expired, or no longer active.");
        }

        if (refreshTokenEntity.User is null)
        {
            throw new InvalidOperationException("The refresh token is no longer associated with a user.");
        }

        refreshTokenEntity.RevokedAt = utcNow;
        refreshTokenEntity.LastUsedAt = utcNow;

        var response = await IssueTokenSetAsync(refreshTokenEntity.User, refreshTokenEntity.ClientId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var tokenHash = HashToken(refreshToken.Trim());
        var refreshTokenEntity = await _dbContext.ExtensionRefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (refreshTokenEntity is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(clientId)
            && !string.Equals(refreshTokenEntity.ClientId, clientId.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        if (!refreshTokenEntity.RevokedAt.HasValue)
        {
            refreshTokenEntity.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ExtensionTokenResponse> IssueTokenSetAsync(
        User user,
        string clientId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var accessTokenExpiresAt = utcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var refreshTokenExpiresAt = utcNow.AddDays(_options.RefreshTokenLifetimeDays);
        var rawRefreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));

        _dbContext.ExtensionRefreshTokens.Add(new ExtensionRefreshToken
        {
            UserId = user.Id,
            ClientId = clientId,
            TokenHash = HashToken(rawRefreshToken),
            CreatedAt = utcNow,
            ExpiresAt = refreshTokenExpiresAt
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ExtensionTokenResponse
        {
            AccessToken = CreateAccessToken(user, clientId, accessTokenExpiresAt),
            ExpiresIn = (int)Math.Round((accessTokenExpiresAt - utcNow).TotalSeconds),
            RefreshToken = rawRefreshToken,
            RefreshExpiresIn = (int)Math.Round((refreshTokenExpiresAt - utcNow).TotalSeconds),
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                CreatedAt = user.CreatedAt
            }
        };
    }

    private string CreateAccessToken(User user, string clientId, DateTime expiresAtUtc)
    {
        var signingKey = ExtensionAuthSigningKeyResolver.ResolveSigningKey(_options, _environment);
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email ?? user.UserName ?? user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("client_id", clientId)
        };

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string refreshToken)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(refreshToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return WebEncoders.Base64UrlEncode(hashBytes);
    }

    private static bool ValidatePkce(string codeVerifier, string expectedChallenge)
    {
        var verifierBytes = Encoding.UTF8.GetBytes(codeVerifier.Trim());
        var challengeBytes = SHA256.HashData(verifierBytes);
        var computedChallenge = WebEncoders.Base64UrlEncode(challengeBytes);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedChallenge),
            Encoding.UTF8.GetBytes(expectedChallenge));
    }
}