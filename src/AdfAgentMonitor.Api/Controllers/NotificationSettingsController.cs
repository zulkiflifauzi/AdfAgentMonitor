using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AdfAgentMonitor.Api.Controllers;

[ApiController]
[Route("api/settings/notifications")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class NotificationSettingsController(
    INotificationSettingsRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var s = await repo.GetAsync(ct);
        return Ok(new { s.RecipientEmail });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UpdateRecipientRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RecipientEmail))
            return BadRequest("RecipientEmail is required.");

        var s = new NotificationSettings { RecipientEmail = req.RecipientEmail.Trim() };
        await repo.SaveAsync(s, ct);
        return Ok(new { s.RecipientEmail });
    }
}

public record UpdateRecipientRequest(string RecipientEmail);
