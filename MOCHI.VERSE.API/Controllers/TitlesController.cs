using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TitlesController : ControllerBase
{
    private readonly TitleService _titleService;
    private readonly ILogger<TitlesController> _logger;

    public TitlesController(TitleService titleService, ILogger<TitlesController> logger)
    {
        _titleService = titleService;
        _logger = logger;
    }

    [HttpGet("search")]
    [AllowAnonymous]
    [EnableRateLimiting("titles")]
    public async Task<ActionResult<APIResponse>> Search(
        [FromQuery] string? q,
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length > 100)
        {
            return BadRequest(Master.CreateAPIResponse(false, "Query parameter 'q' is required (max 100 characters)."));
        }

        if (page is < 1 or > 100)
        {
            return BadRequest(Master.CreateAPIResponse(false, "Parameter 'page' must be between 1 and 100."));
        }

        try
        {
            var results = await _titleService.SearchAsync(q, type, page, cancellationToken);
            return Ok(Master.CreateAPIResponse(true, data: results));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Provider configuration error during title search.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Master.CreateAPIResponse(false, ex.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "External API error during title search.");
            return StatusCode(StatusCodes.Status502BadGateway,
                Master.CreateAPIResponse(false, "Failed to fetch results from external provider."));
        }
    }

    [HttpGet("{source}/{id}")]
    [AllowAnonymous]
    [EnableRateLimiting("titles")]
    public async Task<ActionResult<APIResponse>> GetDetail(
        string source,
        string id,
        CancellationToken cancellationToken = default)
    {
        var userId = User.Identity?.IsAuthenticated == true ? Master.GetUserId(User) : null;
        if (userId is not null)
        {
            Response.Headers.CacheControl = "no-store";
        }

        try
        {
            var detail = await _titleService.GetDetailAsync(source, id, userId, cancellationToken);
            if (detail is null)
            {
                return NotFound(Master.CreateAPIResponse(false, "Title not found."));
            }

            return Ok(Master.CreateAPIResponse(true, data: detail));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Master.CreateAPIResponse(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Provider configuration error during title detail lookup.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Master.CreateAPIResponse(false, ex.Message));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during title detail lookup.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "External API error during title detail lookup.");
            return StatusCode(StatusCodes.Status502BadGateway,
                Master.CreateAPIResponse(false, "Failed to fetch title from external provider."));
        }
    }
}
