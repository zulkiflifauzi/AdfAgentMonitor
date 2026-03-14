using AdfAgentMonitor.Api.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace AdfAgentMonitor.Api.Controllers;

/// <summary>
/// Lightweight health-check endpoint used by the dashboard Settings page
/// to verify API reachability and key validity.
/// </summary>
[ApiController]
[Route("api/health")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class HealthController : ControllerBase
{
    /// <summary>Returns 200 OK when the API is reachable and the key is valid.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Get() =>
        Ok(new
        {
            status    = "ok",
            timestamp = DateTimeOffset.UtcNow,
        });
}
