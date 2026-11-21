using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Controllers;

[ApiController]
[Route("api/me/watchlist")]
[Authorize]
public sealed class WatchlistController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "PlanToWatch",
        "Watching",
        "Completed"
    };

    private readonly IWatchlistRepository _watchlistRepository;
    private readonly TitleService _titleService;
    private readonly ILogger<WatchlistController> _logger;

    public WatchlistController(
        IWatchlistRepository watchlistRepository,
        TitleService titleService,
        ILogger<WatchlistController> logger)
    {
        _watchlistRepository = watchlistRepository;
        _titleService = titleService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<APIResponse>> GetMyWatchlist(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = Master.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized(Master.CreateAPIResponse(false, "Invalid token."));
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = status.Trim();
            if (!AllowedStatuses.Contains(normalizedStatus))
            {
                return BadRequest(Master.CreateAPIResponse(false,
                    "Invalid status. Allowed values: 'PlanToWatch', 'Watching', 'Completed'."));
            }
        }

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
            var data = await _watchlistRepository.GetPageByUserAsync(
                userId.Value, normalizedStatus, page, pageSize, cancellationToken);
            return Ok(Master.CreateAPIResponse(true, data: data));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while loading user watchlist.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpPut("{source}/{id}")]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<APIResponse>> UpsertWatchlist(
        string source,
        string id,
        [FromBody] UpsertWatchlistRequest request,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

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

            var saved = await _watchlistRepository.UpsertAsync(
                userId.Value, title.Source, title.ExternalId, request.Status, cancellationToken);

            return Ok(Master.CreateAPIResponse(true, "Watchlist updated.", saved));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Provider configuration error while caching title for watchlist.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Master.CreateAPIResponse(false, ex.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "External API error while caching title for watchlist.");
            return StatusCode(StatusCodes.Status502BadGateway,
                Master.CreateAPIResponse(false, "Failed to verify title from external provider."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while updating watchlist.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpDelete("{source}/{id}")]
    [EnableRateLimiting("write")]
    public async Task<ActionResult<APIResponse>> DeleteWatchlist(
        string source,
        string id,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = Master.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized(Master.CreateAPIResponse(false, "Invalid token."));
        }

        try
        {
            var (normalizedSource, normalizedId) = _titleService.ValidateTitleKey(source, id);
            var deleted = await _watchlistRepository.DeleteAsync(userId.Value, normalizedSource, normalizedId, cancellationToken);
            if (!deleted)
            {
                return NotFound(Master.CreateAPIResponse(false, "Watchlist entry not found."));
            }

            return Ok(Master.CreateAPIResponse(true, "Watchlist entry removed."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while deleting watchlist entry.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }
}
