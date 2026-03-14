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
        return Ok(new { RecipientEmails = s.RecipientEmailList });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UpdateRecipientsRequest req, CancellationToken ct)
    {
        var emails = (req.RecipientEmails ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (emails.Count == 0)
            return BadRequest("At least one recipient email is required.");

        var s = new NotificationSettings();
        s.RecipientEmailList = emails;
        await repo.SaveAsync(s, ct);
        return Ok(new { RecipientEmails = s.RecipientEmailList });
    }
}

public record UpdateRecipientsRequest(List<string>? RecipientEmails);
