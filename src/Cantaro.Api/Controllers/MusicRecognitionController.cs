using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Configuration;
using Microsoft.Extensions.Options;
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
    TrackMatchQueue trackMatchQueue,
    MusicLibraryQueryService musicLibraryQueryService,
    UserManager<User> userManager,
    IOptions<TrackMatchingOptions>? matchingOptions = null) : ControllerBase
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

        var existing = await dbContext.TrackObservations.Include(o => o.ProviderCatalogItem)
            .FirstOrDefaultAsync(x => x.OwnerUserId == user.Id && x.SourceType == "youtube" && x.ExternalId == videoId, cancellationToken);
        if (existing is { TrackId: { } existingTrackId } && await CanReuseResultAsync(existing, user.Id, cancellationToken))
            return Ok(await BuildMatchedResponse(existingTrackId, user.Id, cancellationToken));

        var source = await new ProviderRecordingAssociations(dbContext).FindUsableAsync("youtube", videoId, cancellationToken);
        if (source is not null) return Ok(await BuildMatchedResponse(source.TrackId!.Value, user.Id, cancellationToken));


        if (existing is not null && existing.TrackId == null && ProviderCatalogPolicy.HasOwnedInput(existing, user.Id)
            && ShouldBackOff(existing, includeFreshPending: false))
            return Ok(new RecognizeYouTubeResponse("music", existing.MatchStatus, null, ObservationInput.Read(existing, user.Id, DateTimeOffset.UtcNow)?.Title, ObservationInput.Read(existing, user.Id, DateTimeOffset.UtcNow)?.Artist, false, null));

        YouTubeVideoMetadataDto? metadata = null;
        try { metadata = await youtubeService.GetVideoMetadataAsync(user.Id, videoId, cancellationToken); }
        catch (Google.GoogleApiException) { }
        if (metadata is null)
        {
            return Ok(new RecognizeYouTubeResponse("uncertain", "pending", null, null, null, false, null));
        }
        var classification = Classify(request.Site, metadata);
        if (classification != "music")
            return Ok(new RecognizeYouTubeResponse(classification, "ignored", null, null, null, false, null));
        if (string.IsNullOrWhiteSpace(metadata.Title))
            return Ok(new RecognizeYouTubeResponse("uncertain", "pending", null, null, null, false, null));

        var now = DateTimeOffset.UtcNow;
        var parsedMetadata = TrackMetadataParser.Parse(
            metadata.Title,
            metadata.ChannelTitle);
        var observation = existing ?? new TrackObservation
        {
            Id = Guid.NewGuid(), OwnerUserId = user.Id, SourceType = "youtube", ExternalId = videoId,
            Title = parsedMetadata.DisplayTitle,
            Artist = parsedMetadata.DisplayArtist,
            ThumbnailUrl = metadata.ThumbnailUrl,
            DurationSeconds = metadata.DurationSeconds, MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = now, UpdatedAt = now
        };
        if (metadata.ProviderCatalogItemId is { } catalogId)
            ObservationInput.SetPublic(observation, await dbContext.ProviderCatalogItems.SingleAsync(i => i.Id == catalogId, cancellationToken));
        else
            ObservationInput.SetPrivate(observation, parsedMetadata.DisplayTitle, parsedMetadata.DisplayArtist,
                metadata.DurationSeconds, metadata.ThumbnailUrl, new TrackObservationMetadata
                {
                    OriginalTitle = metadata.Title, OriginalArtist = metadata.ChannelTitle,
                    ChannelTitle = metadata.ChannelTitle, Description = metadata.Description
                });
        if (!await CanReuseResultAsync(observation, user.Id, cancellationToken))
        {
            observation.TrackId = null;
            observation.MatchStatus = TrackMatchingStatuses.Pending;
            observation.AcceptedCandidateId = null;
        }
        observation.UpdatedAt = now;
        if (existing is null)
        {
            dbContext.TrackObservations.Add(observation);
            try { await dbContext.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException)
            {
                dbContext.Entry(observation).State = EntityState.Detached;
                observation = await dbContext.TrackObservations.Include(o => o.ProviderCatalogItem)
                    .SingleAsync(x => x.OwnerUserId == user.Id && x.SourceType == "youtube" && x.ExternalId == videoId, cancellationToken);
                if (await CanReuseResultAsync(observation, user.Id, cancellationToken) && observation.TrackId is Guid concurrentTrackId)
                    return Ok(await BuildMatchedResponse(concurrentTrackId, user.Id, cancellationToken));
                if (ShouldBackOff(observation, includeFreshPending: true))
                    return Ok(new RecognizeYouTubeResponse("music", observation.MatchStatus, null, ObservationInput.Read(observation, user.Id, DateTimeOffset.UtcNow)?.Title, ObservationInput.Read(observation, user.Id, DateTimeOffset.UtcNow)?.Artist, false, null));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await trackMatchQueue.EnqueueAsync(observation.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (observation.TrackId is { } refreshedTrackId)
            return Ok(await BuildMatchedResponse(refreshedTrackId, user.Id, cancellationToken));
        return Ok(new RecognizeYouTubeResponse("music", TrackMatchingStatuses.Pending, null, ObservationInput.Read(observation, user.Id, DateTimeOffset.UtcNow)?.Title, ObservationInput.Read(observation, user.Id, DateTimeOffset.UtcNow)?.Artist, false, null));
    }

    private Task<bool> CanReuseResultAsync(TrackObservation observation, int userId, CancellationToken ct) =>
        new ProviderRecordingAssociations(dbContext, matchingOptions?.Value).CanReuseObservationResultAsync(observation, userId, ct);

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
        var track = await dbContext.Tracks.AsNoTracking().Include(item => item.ProviderItems).FirstAsync(x => x.Id == trackId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var metadata = ProviderCatalogPolicy.SharedFacts(track, now);
        var owned = await ObservationInput.Eligible(dbContext.TrackObservations.AsNoTracking(), now)
            .Include(item => item.ProviderCatalogItem)
            .Where(item => item.TrackId == trackId && item.OwnerUserId == userId)
            .OrderBy(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        var input = ObservationInput.Read(owned, userId, now);
        var inLibrary = await dbContext.PlaylistEntries.AsNoTracking()
            .AnyAsync(x => x.TrackId == trackId && x.Playlist != null && x.Playlist.UserId == userId, cancellationToken);
        return new RecognizeYouTubeResponse("music", "matched", trackId, metadata?.Title ?? input?.Title, metadata?.Artist ?? input?.Artist, inLibrary,
            await musicLibraryQueryService.GetCanonicalSongAsync(trackId, userId, cancellationToken));
    }

    private async Task<RecognizeYouTubeResponse?> BuildSharedCanonicalResponseAsync(
        Guid trackId, int userId, CancellationToken cancellationToken)
    {
        var track = await dbContext.Tracks.AsNoTracking().Include(item => item.ProviderItems).FirstOrDefaultAsync(x => x.Id == trackId, cancellationToken);
        if (track is null) return null;
        var metadata = ProviderCatalogPolicy.SharedFacts(track, DateTimeOffset.UtcNow);
        if (metadata is null) return null;
        var inLibrary = await dbContext.PlaylistEntries.AsNoTracking()
            .AnyAsync(x => x.TrackId == trackId && x.Playlist != null && x.Playlist.UserId == userId, cancellationToken);
        return new RecognizeYouTubeResponse("music", "matched", trackId, metadata.Title, metadata.Artist, inLibrary,
            await musicLibraryQueryService.GetCanonicalSongAsync(trackId, userId, cancellationToken));
    }
}
