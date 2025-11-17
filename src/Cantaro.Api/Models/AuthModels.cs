using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Models;

public class RegisterRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
    
    [Required]
    [MinLength(6)]
    public required string Password { get; set; }
}

public class LoginRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; set; }
    
    [Required]
    public required string Password { get; set; }
}

public class AuthResponse
{
    public required string Token { get; set; }
    public required UserDto User { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }
}
