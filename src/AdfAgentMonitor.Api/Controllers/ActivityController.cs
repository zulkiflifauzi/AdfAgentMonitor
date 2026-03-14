using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdfAgentMonitor.Api.Controllers;

/// <summary>
/// Exposes agent activity log entries for the dashboard timeline.
/// </summary>
[ApiController]
[Route("api/activity")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class ActivityController(
    IAgentActivityLogRepository repository,
    ILogger<ActivityController> logger) : ControllerBase
{
    /// <summary>
    /// Returns a page of agent activity log entries, newest first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string?         agentName = null,
        [FromQuery] bool?           success   = null,
        [FromQuery] DateTimeOffset? from      = null,
        [FromQuery] DateTimeOffset? to        = null,
        [FromQuery] int             page      = 1,
        [FromQuery] int             pageSize  = 50,
        CancellationToken ct = default)
    {
        if (page < 1)
            return BadRequest(new { error = "page must be >= 1." });

        if (pageSize is < 1 or > 200)
            return BadRequest(new { error = "pageSize must be between 1 and 200." });

        var (items, total) = await repository.GetPagedAsync(
            agentName, success, from, to, page, pageSize, ct);

        logger.LogDebug(
            "ActivityController returned {Count}/{Total} entries (page={Page}).",
            items.Count, total, page);

        return Ok(new
        {
            items,
            totalCount = total,
            page,
            pageSize,
        });
    }
}
