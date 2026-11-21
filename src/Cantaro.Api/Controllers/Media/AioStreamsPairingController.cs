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
        var pairing = await db.Set<AioStreamsPairing>().Where(p => p.UserId == userId)
            .Select(p => new { p.PlaybackInstanceUrl }).SingleOrDefaultAsync(cancellationToken);
        return Ok(new { paired = pairing is not null, playbackInstanceUrl = pairing?.PlaybackInstanceUrl });
    }

    [HttpPut("playback")]
    public async Task<IActionResult> SetPlaybackInstance(PlaybackSettings settings, CancellationToken cancellationToken)
    {
        if (!int.TryParse(users.GetUserId(User), out var userId)) return Unauthorized();
        var value = settings.InstanceUrl?.Trim();
        string? normalized = null;
        if (!string.IsNullOrEmpty(value))
        {
            if (value.Length > 2048 || value.Contains('?') || value.Contains('#') || value.Contains('\\')
                || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0
                || uri.AbsolutePath.Split('/').Any(segment => segment.Equals("jellyfin", StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new { error = "Enter the HTTPS address of your AIOStreams instance." });
            normalized = uri.AbsoluteUri.TrimEnd('/');
            if (normalized.Length > 2048) return BadRequest();
        }

        var updated = await db.Set<AioStreamsPairing>().Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.PlaybackInstanceUrl, normalized), cancellationToken);
        return updated == 0 ? Conflict() : Ok(new { playbackInstanceUrl = normalized });
    }

    [HttpPut]
    public async Task<IActionResult> Pair(CancellationToken cancellationToken)
    {
        if (!int.TryParse(users.GetUserId(User), out var userId)) return Unauthorized();
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash = AioStreamsWatchState.HashToken(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AioStreamsPairings" ("UserId", "TokenHash") VALUES ({userId}, {hash})
            ON CONFLICT ("UserId") DO UPDATE SET "TokenHash" = EXCLUDED."TokenHash"
            """, cancellationToken);
        return Ok(new { manifestPath = $"{AioStreamsWatchState.Route}/manifest.json?token={token}" });
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        if (!int.TryParse(users.GetUserId(User), out var userId)) return Unauthorized();
        await db.Set<AioStreamsPairing>().Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    public sealed record PlaybackSettings(string? InstanceUrl);
}
