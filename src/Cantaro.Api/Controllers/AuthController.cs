using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.RegisterAsync(request);
        if (result == null)
        {
            return BadRequest(new { message = "Registration failed. Please try again." });
        }

        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.LoginAsync(request);
        if (result == null)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userIdClaim == null || !int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        var user = await _authService.GetUserByIdAsync(userId);
        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        return Ok(user);
    }
}
