using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    
    public AuthController(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var user = await _userManager.FindByEmailAsync(User.Identity?.Name ?? "");

        if (user == null)
        {
            return NotFound("User not found");
        }

        var dto = new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? "Could not retrieve email",
            CreatedAt = user.CreatedAt
        };

        return Ok(dto);
    }
}
