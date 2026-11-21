using System.ComponentModel.DataAnnotations;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/media/titles/{mediaTitleId:guid}/crunchyroll-mappings")]
public sealed class MediaCrunchyrollMappingsController(
    ApplicationDbContext dbContext,
    MediaProviderSeasonMappingService mappings) : ControllerBase
{
    private const string Provider = MediaObservationSiteIdentifiers.Crunchyroll;

    [HttpGet]
    public async Task<IActionResult> List(Guid mediaTitleId, CancellationToken cancellationToken)
    {
        if (!await dbContext.MediaTitles.AnyAsync(title => title.Id == mediaTitleId, cancellationToken))
        {
            return NotFound();
        }

        var seasons = await dbContext.MediaProviderSeasonMappings.AsNoTracking()
            .Where(row => row.Provider == Provider && row.MediaTitleId == mediaTitleId)
            .OrderBy(row => row.ProviderSeriesId).ThenBy(row => row.ProviderSeasonKey)
            .ThenBy(row => row.ProviderEpisodeStart)
            .Select(SeasonResponse).ToListAsync(cancellationToken);
        var episodes = await dbContext.MediaProviderEpisodeMappings.AsNoTracking()
            .Where(row => row.Provider == Provider && row.MediaTitleId == mediaTitleId)
            .OrderBy(row => row.ProviderEpisodeId)
            .Select(row => new MediaProviderEpisodeMappingDto(row.Provider, row.ProviderEpisodeId,
                row.MediaTitleId, row.CanonicalEpisodeNumber, row.MappingSource))
            .ToListAsync(cancellationToken);
        return Ok(new { seasons, episodes });
    }

    [HttpPut("seasons")]
    public async Task<IActionResult> PutSeason(Guid mediaTitleId, SeasonMappingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mappings.WithWriteAsync<IActionResult>(async () =>
            {
                if (!await dbContext.MediaTitles.AnyAsync(title => title.Id == mediaTitleId, cancellationToken))
                {
                    return NotFound();
                }

                var mapping = await mappings.EstablishAsync(Provider, request.ProviderSeriesId, request.ProviderSeasonId,
                    request.ProviderSeasonNumber, mediaTitleId, request.EpisodeOffset,
                    MediaProviderSeasonMappingSources.Manual, 1m, cancellationToken,
                    request.SeasonTitle, request.ProviderEpisodeStart, request.ProviderEpisodeEnd);
                return mapping is null ? BadRequest(new { error = "A valid Crunchyroll series and season are required." })
                    : Ok(MapSeasonResponse(mapping));
            }, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPut("episodes/{providerEpisodeId}")]
    public async Task<IActionResult> PutEpisode(Guid mediaTitleId, string providerEpisodeId,
        EpisodeMappingRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await mappings.EstablishManualDirectAsync(Provider, providerEpisodeId, mediaTitleId,
                request.CanonicalEpisodeNumber, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpDelete("seasons/{mappingId:guid}")]
    public Task<IActionResult> DeleteSeason(Guid mediaTitleId, Guid mappingId, CancellationToken cancellationToken) =>
        DeleteAsync(dbContext.MediaProviderSeasonMappings.Where(row => row.Id == mappingId
            && row.Provider == Provider && row.MediaTitleId == mediaTitleId), cancellationToken);

    [HttpDelete("episodes/{providerEpisodeId}")]
    public Task<IActionResult> DeleteEpisode(Guid mediaTitleId, string providerEpisodeId, CancellationToken cancellationToken)
    {
        var id = MediaProviderSeasonMappingService.NormalizeId(providerEpisodeId);
        return DeleteAsync(dbContext.MediaProviderEpisodeMappings.Where(row => row.ProviderEpisodeId == id
            && row.Provider == Provider && row.MediaTitleId == mediaTitleId), cancellationToken);
    }

    private async Task<IActionResult> DeleteAsync<TMapping>(IQueryable<TMapping> query, CancellationToken cancellationToken)
        where TMapping : class
    {
        return await mappings.WithWriteAsync<IActionResult>(async () =>
            await query.ExecuteDeleteAsync(cancellationToken) == 0 ? NotFound() : NoContent(), cancellationToken);
    }

    private static readonly System.Linq.Expressions.Expression<Func<MediaProviderSeasonMapping, MediaProviderSeasonMappingDto>>
        SeasonResponse = row => new(row.Id, row.Provider, row.MediaTitleId, row.ProviderSeriesId,
            row.ProviderSeasonId, row.ProviderSeasonNumber, row.NormalizedSeasonTitle, row.EpisodeOffset,
            row.ProviderEpisodeStart, row.ProviderEpisodeEnd, row.MappingSource);
    private static readonly Func<MediaProviderSeasonMapping, MediaProviderSeasonMappingDto> MapSeasonResponse =
        SeasonResponse.Compile();

    public sealed record MediaProviderSeasonMappingDto(Guid Id, string Provider, Guid MediaTitleId,
        string ProviderSeriesId, string? ProviderSeasonId, int? ProviderSeasonNumber, string? NormalizedSeasonTitle,
        int EpisodeOffset, int? ProviderEpisodeStart, int? ProviderEpisodeEnd, string MappingSource);

    public sealed record MediaProviderEpisodeMappingDto(string Provider, string ProviderEpisodeId, Guid MediaTitleId,
        int? EpisodeNumber, string MappingSource);

    public sealed class SeasonMappingRequest
    {
        [Required, MaxLength(256)]
        public required string ProviderSeriesId { get; init; }

        [MaxLength(256)]
        public string? ProviderSeasonId { get; init; }

        [Range(1, int.MaxValue)]
        public int? ProviderSeasonNumber { get; init; }

        [MaxLength(512)]
        public string? SeasonTitle { get; init; }

        [Range(1, int.MaxValue)]
        public int? ProviderEpisodeStart { get; init; }

        [Range(1, int.MaxValue)]
        public int? ProviderEpisodeEnd { get; init; }

        public int EpisodeOffset { get; init; }
    }

    public sealed class EpisodeMappingRequest
    {
        [Range(1, int.MaxValue)]
        public int? CanonicalEpisodeNumber { get; init; }
    }
}
