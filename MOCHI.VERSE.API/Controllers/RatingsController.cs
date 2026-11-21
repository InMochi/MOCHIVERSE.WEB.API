using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Controllers;

[ApiController]
[Route("api/titles/{source}/{id}")]
public sealed class RatingsController : ControllerBase
{
    private readonly IRatingRepository _ratingRepository;
    private readonly ITitleCacheRepository _titleCacheRepository;
    private readonly TitleService _titleService;
    private readonly ILogger<RatingsController> _logger;

    public RatingsController(
        IRatingRepository ratingRepository,
        ITitleCacheRepository titleCacheRepository,
        TitleService titleService,
        ILogger<RatingsController> logger)
    {
        _ratingRepository = ratingRepository;
        _titleCacheRepository = titleCacheRepository;
        _titleService = titleService;
        _logger = logger;
    }

    [HttpPut("rating")]
    [Authorize]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<APIResponse>> UpsertRating(
        string source,
        string id,
        [FromBody] UpsertRatingRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = Master.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized(Master.CreateAPIResponse(false, "Invalid token."));
        }

        try
        {
            var title = await _titleService.EnsureTitleCachedAsync(source, id, cancellationToken);
            if (title is null)
            {
                return NotFound(Master.CreateAPIResponse(false, "Title not found."));
            }

            var review = string.IsNullOrWhiteSpace(request.Review) ? null : request.Review.Trim();
            var saved = await _ratingRepository.UpsertAsync(
                userId.Value, title.Source, title.ExternalId, request.Score, review, cancellationToken);

            return Ok(Master.CreateAPIResponse(true, "Rating saved.", saved));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Provider configuration error while caching title for rating.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Master.CreateAPIResponse(false, ex.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "External API error while caching title for rating.");
            return StatusCode(StatusCodes.Status502BadGateway,
                Master.CreateAPIResponse(false, "Failed to verify title from external provider."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while saving rating.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpDelete("rating")]
    [Authorize]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<APIResponse>> DeleteRating(
        string source,
        string id,
        CancellationToken cancellationToken = default)
    {
        var userId = Master.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized(Master.CreateAPIResponse(false, "Invalid token."));
        }

        try
        {
            var (normalizedSource, normalizedId) = _titleService.ValidateTitleKey(source, id);
            var deleted = await _ratingRepository.DeleteAsync(userId.Value, normalizedSource, normalizedId, cancellationToken);
            if (!deleted)
            {
                return NotFound(Master.CreateAPIResponse(false, "Rating not found."));
            }

            return Ok(Master.CreateAPIResponse(true, "Rating deleted."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while deleting rating.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpGet("ratings")]
    [AllowAnonymous]
    public async Task<ActionResult<APIResponse>> GetRatings(
        string source,
        string id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            return BadRequest(Master.CreateAPIResponse(false, "Parameter 'page' must be at least 1."));
        }

        if (pageSize is < 1 or > 50)
        {
            return BadRequest(Master.CreateAPIResponse(false, "Parameter 'pageSize' must be between 1 and 50."));
        }

        try
        {
            var (normalizedSource, normalizedId) = _titleService.ValidateTitleKey(source, id);
            var summary = await _titleCacheRepository.GetRatingSummaryAsync(normalizedSource, normalizedId, cancellationToken);
            var items = await _ratingRepository.GetByTitleAsync(normalizedSource, normalizedId, page, pageSize, cancellationToken);

            var data = new TitleRatingsPage
            {
                Source = normalizedSource,
                ExternalId = normalizedId,
                TotalVotes = summary.TotalVotes,
                AverageScore = summary.AverageScore,
                Page = page,
                PageSize = pageSize,
                TotalItems = summary.TotalVotes,
                Items = items
            };

            return Ok(Master.CreateAPIResponse(true, data: data));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while loading title ratings.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }
}
