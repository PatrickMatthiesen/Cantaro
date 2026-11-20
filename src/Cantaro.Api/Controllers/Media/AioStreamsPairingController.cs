using System.Security.Cryptography;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/media/aiostreams-pairing")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AioStreamsPairingController(ApplicationDbContext db, UserManager<User> users) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        if (!int.TryParse(users.GetUserId(User), out var userId)) return Unauthorized();
        return Ok(new { paired = await db.Set<AioStreamsPairing>().AnyAsync(p => p.UserId == userId, cancellationToken) });
    }

    [HttpPut]
    public async Task<IActionResult> Pair(CancellationToken cancellationToken)
    {
        if (!int.TryParse(users.GetUserId(User), out var userId)) return Unauthorized();
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash = AioStreamsWatchStateOptions.HashToken(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AioStreamsPairings" ("UserId", "TokenHash") VALUES ({userId}, {hash})
            ON CONFLICT ("UserId") DO UPDATE SET "TokenHash" = EXCLUDED."TokenHash"
            """, cancellationToken);
        return Ok(new { manifestPath = $"{AioStreamsWatchStateOptions.Route}/manifest.json?token={token}" });
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        if (!int.TryParse(users.GetUserId(User), out var userId)) return Unauthorized();
        await db.Set<AioStreamsPairing>().Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }
}
