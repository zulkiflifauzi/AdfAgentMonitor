using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdfAgentMonitor.Api.Controllers;

/// <summary>
/// Exposes pipeline run state data for the dashboard.
/// </summary>
[ApiController]
[Route("api/runs")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class RunsController(
    IPipelineRunStateRepository repository,
    ILogger<RunsController> logger) : ControllerBase
{
    // -------------------------------------------------------------------------
    // GET /api/runs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a page of <c>PipelineRunState</c> rows, newest first.
    /// All filter parameters are optional and combinable.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] PipelineRunStatus? status   = null,
        [FromQuery] RemediationRisk?   risk     = null,
        [FromQuery] string?            name     = null,
        [FromQuery] DateTimeOffset?    fromDate = null,
        [FromQuery] DateTimeOffset?    toDate   = null,
        [FromQuery] int                page     = 1,
        [FromQuery] int                pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1)
            return BadRequest(new { error = "page must be >= 1." });

        if (pageSize is < 1 or > 200)
            return BadRequest(new { error = "pageSize must be between 1 and 200." });

        var (items, total) = await repository.GetPagedAsync(
            status, risk, name, fromDate, toDate, page, pageSize, ct);

        logger.LogDebug(
            "RunsController.GetAsync returned {Count}/{Total} rows (page={Page}).",
            items.Count, total, page);

        return Ok(new
        {
            items,
            totalCount = total,
            page,
            pageSize,
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/runs/summary
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns aggregated counts for the dashboard stat cards.
    /// The route literal <c>summary</c> is resolved before the <c>{id:guid}</c>
    /// constraint so there is no ambiguity.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSummaryAsync(CancellationToken ct)
    {
        var summary = await repository.GetSummaryAsync(ct);

        logger.LogDebug(
            "RunsController.GetSummaryAsync: total={Total} failed={Failed} " +
            "remediated={Remediated} pending={Pending}.",
            summary.TotalToday, summary.FailedToday,
            summary.RemediatedToday, summary.PendingApproval);

        return Ok(summary);
    }

    // -------------------------------------------------------------------------
    // GET /api/runs/{id}
    // -------------------------------------------------------------------------

    /// <summary>Returns a single <c>PipelineRunState</c> by its primary key.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var state = await repository.GetByIdAsync(id, ct);

        if (state is null)
            return NotFound(new { error = $"PipelineRunState {id} not found." });

        return Ok(state);
    }
}
