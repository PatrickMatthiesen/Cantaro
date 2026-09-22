using System.Text.Json;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Cantaro.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SessionController(SignInManager<User> signInManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("logout")]
    [Consumes("application/json")]
    // Requiring JSON prevents cross-site HTML forms from triggering logout.
    public async Task<IActionResult> Logout([FromBody] JsonElement _)
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }
}
