using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;

namespace Cantaro.Api.Services;

public interface IAuthService
{
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<UserDto?> GetUserByIdAsync(int userId);
}

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        // Normalize email to lowercase
        var normalizedEmail = request.Email.ToLowerInvariant();
        
        // Check if user already exists (case-insensitive)
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
        {
            return null;
        }

        // Hash the password with explicit work factor
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        // Create new user
        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Generate JWT token
        var token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Token = token,
            User = MapToUserDto(user)
        };
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        // Normalize email to lowercase for case-insensitive comparison
        var normalizedEmail = request.Email.ToLowerInvariant();
        
        // Find user by email (case-insensitive)
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        if (user == null)
        {
            return null;
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return null;
        }

        // Generate JWT token
        var token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Token = token,
            User = MapToUserDto(user)
        };
    }

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user == null ? null : MapToUserDto(user);
    }

    private string GenerateJwtToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured");
        var jwtIssuer = _configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("JWT Issuer not configured");
        var jwtAudience = _configuration["Jwt:Audience"] ?? throw new InvalidOperationException("JWT Audience not configured");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static UserDto MapToUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }
}
