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
            normalized = NormalizePlaybackInstance(value);
            if (normalized is null)
                return BadRequest(new { error = "Enter the HTTPS address of your AIOStreams instance." });
        }

        var updated = await db.Set<AioStreamsPairing>().Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.PlaybackInstanceUrl, normalized), cancellationToken);
        return updated == 0 ? Conflict() : Ok(new { playbackInstanceUrl = normalized });
    }

    internal static string? NormalizePlaybackInstance(string value)
    {
        if (value.Length > 2048 || value.Contains('?') || value.Contains('#') || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0)
            return null;

        var segments = Uri.UnescapeDataString(uri.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Equals("jellyfin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                return null;

            if (segment.Equals("stremio", StringComparison.OrdinalIgnoreCase)
                && index + 1 < segments.Length
                && (Guid.TryParseExact(segments[index + 1], "D", out _)
                    || (segments[index + 1].Equals("u", StringComparison.OrdinalIgnoreCase) && index + 2 < segments.Length)))
                return null;
        }

        var normalized = uri.AbsoluteUri.TrimEnd('/');
        return normalized.Length <= 2048 ? normalized : null;
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
