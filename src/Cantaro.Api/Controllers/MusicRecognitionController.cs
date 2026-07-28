using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Cantaro.Api.Controllers;

public sealed record RecognizeYouTubeRequest(string VideoId, string Site, string? PageTitle);
public sealed record RecognizeYouTubeResponse(string Classification, string Status, Guid? TrackId, string? Title, string? Artist, bool InUserLibrary, MusicLibrarySongDto? Song);

[ApiController]
[Route("api/music/recognition")]
[Authorize]
public sealed class MusicRecognitionController(
    ApplicationDbContext dbContext,
    YouTubeService youtubeService,
    TrackMatchingQueue trackMatchingQueue,
    MusicLibraryQueryService musicLibraryQueryService,
    UserManager<User> userManager) : ControllerBase
{
    private static readonly Regex YouTubeVideoIdPattern = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan MatchRetryDelay = TimeSpan.FromMinutes(15);

    [HttpPost("youtube")]
    public async Task<ActionResult<RecognizeYouTubeResponse>> Recognize(
        RecognizeYouTubeRequest request,
        CancellationToken cancellationToken)
    {
        var videoId = request.VideoId?.Trim();
        if (videoId is null || !YouTubeVideoIdPattern.IsMatch(videoId))
            return BadRequest(new { error = "A valid YouTube video ID is required." });
        if (!string.Equals(request.Site, "youtube", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Site, "youtube_music", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Unsupported YouTube site." });
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();

        var source = await dbContext.TrackSourceIds.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourceType == "youtube" && x.ExternalId == videoId, cancellationToken);
        if (source is not null) return Ok(await BuildMatchedResponse(source.TrackId, user.Id, cancellationToken));

        var existing = await dbContext.TrackObservations
            .FirstOrDefaultAsync(x => x.SourceType == "youtube" && x.ExternalId == videoId, cancellationToken);
        if (existing?.TrackId is Guid existingTrackId) return Ok(await BuildMatchedResponse(existingTrackId, user.Id, cancellationToken));

        if (existing is not null && ShouldBackOff(existing, includeFreshPending: false))
            return Ok(new RecognizeYouTubeResponse("music", existing.MatchStatus, null, existing.Title, existing.Artist, false, null));

        YouTubeVideoMetadataDto? metadata = null;
        try { metadata = await youtubeService.GetVideoMetadataAsync(user.Id, videoId, cancellationToken); }
        catch (Google.GoogleApiException) { }
        var classification = Classify(request.Site, metadata);
        if (classification != "music")
            return Ok(new RecognizeYouTubeResponse(classification, "ignored", null, null, null, false, null));

        var now = DateTimeOffset.UtcNow;
        var parsedMetadata = TrackMetadataParser.Parse(
            metadata?.Title ?? request.PageTitle ?? videoId,
            metadata?.ChannelTitle);
        var observation = existing ?? new TrackObservation
        {
            Id = Guid.NewGuid(), SourceType = "youtube", ExternalId = videoId,
            Title = parsedMetadata.DisplayTitle,
            Artist = parsedMetadata.DisplayArtist,
            ThumbnailUrl = metadata?.ThumbnailUrl,
            DurationSeconds = metadata?.DurationSeconds, MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = now, UpdatedAt = now
        };
        observation.NormalizedTitle = TrackTextNormalizer.Normalize(parsedMetadata.DisplayTitle);
        observation.NormalizedArtist = TrackTextNormalizer.Normalize(parsedMetadata.DisplayArtist);
        observation.RawMetadata = JsonSerializer.Serialize(metadata);
        if (existing is null)
        {
            dbContext.TrackObservations.Add(observation);
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                dbContext.Entry(observation).State = EntityState.Detached;
                observation = await dbContext.TrackObservations
                    .SingleAsync(x => x.SourceType == "youtube" && x.ExternalId == videoId, cancellationToken);
                if (observation.TrackId is Guid concurrentTrackId)
                    return Ok(await BuildMatchedResponse(concurrentTrackId, user.Id, cancellationToken));
                if (ShouldBackOff(observation, includeFreshPending: true))
                    return Ok(new RecognizeYouTubeResponse("music", observation.MatchStatus, null, observation.Title, observation.Artist, false, null));
            }
        }

        trackMatchingQueue.Enqueue(observation.Id);
        return Ok(new RecognizeYouTubeResponse("music", TrackMatchingStatuses.Pending, null, observation.Title, observation.Artist, false, null));
    }

    public static string Classify(string site, YouTubeVideoMetadataDto? metadata)
    {
        if (site.Equals("youtube_music", StringComparison.OrdinalIgnoreCase)) return "music";
        if (metadata is null) return "uncertain";
        if (metadata.CategoryId == "10") return "music";
        if (metadata.TopicCategories.Any(x => x.Contains("Music", StringComparison.OrdinalIgnoreCase))) return "music";
        return "not_music";
    }

    private static bool ShouldBackOff(TrackObservation observation, bool includeFreshPending)
    {
        var cutoff = DateTimeOffset.UtcNow - MatchRetryDelay;
        if (observation.LastMatchAttemptedAt is DateTimeOffset attemptedAt) return attemptedAt > cutoff;
        return includeFreshPending && observation.MatchStatus == TrackMatchingStatuses.Pending && observation.CreatedAt > cutoff;
    }

    private async Task<RecognizeYouTubeResponse> BuildMatchedResponse(Guid trackId, int userId, CancellationToken cancellationToken)
    {
        var track = await dbContext.Tracks.AsNoTracking().FirstAsync(x => x.Id == trackId, cancellationToken);
        TrackCanonicalMetadata? metadata = null;
        if (!string.IsNullOrWhiteSpace(track.CanonicalMetadata))
            try { metadata = JsonSerializer.Deserialize<TrackCanonicalMetadata>(track.CanonicalMetadata); } catch (JsonException) { }
        var inLibrary = await dbContext.PlaylistEntries.AsNoTracking()
            .AnyAsync(x => x.TrackId == trackId && x.Playlist != null && x.Playlist.UserId == userId, cancellationToken);
        return new RecognizeYouTubeResponse("music", "matched", trackId, metadata?.Title, metadata?.Artist, inLibrary,
            await musicLibraryQueryService.GetCanonicalSongAsync(trackId, userId, cancellationToken));
    }
}
