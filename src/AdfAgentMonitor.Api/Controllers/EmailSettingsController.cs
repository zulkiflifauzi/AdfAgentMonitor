using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Infrastructure.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AdfAgentMonitor.Api.Controllers;

[ApiController]
[Route("api/settings/email")]
[ServiceFilter(typeof(ApiKeyAuthFilter))]
public class EmailSettingsController(
    IEmailSettingsOverrideRepository repo,
    IEmailNotifierService notifier,
    IOptions<EmailSettings> baseSettings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var ov  = await repo.GetAsync(ct);
        var cfg = baseSettings.Value;
        return Ok(new EmailSettingsResponse(
            SmtpHost:         ov?.SmtpHost         ?? cfg.SmtpHost,
            SmtpPort:         ov?.SmtpPort         ?? cfg.SmtpPort,
            UseSsl:           ov?.UseSsl           ?? cfg.UseSsl,
            Username:         ov?.Username         ?? cfg.Username,
            HasPassword:      !string.IsNullOrEmpty(ov?.Password ?? cfg.Password),
            FromAddress:      ov?.FromAddress      ?? cfg.FromAddress,
            FromName:         ov?.FromName         ?? cfg.FromName,
            DashboardBaseUrl: ov?.DashboardBaseUrl ?? cfg.DashboardBaseUrl,
            HasOverrides:     ov is not null));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UpdateEmailSettingsRequest req, CancellationToken ct)
    {
        var ov = new EmailSettingsOverride
        {
            SmtpHost         = NullIfBlank(req.SmtpHost),
            SmtpPort         = req.SmtpPort,
            UseSsl           = req.UseSsl,
            Username         = NullIfBlank(req.Username),
            Password         = NullIfBlank(req.Password),   // null = keep existing
            FromAddress      = NullIfBlank(req.FromAddress),
            FromName         = NullIfBlank(req.FromName),
            DashboardBaseUrl = NullIfBlank(req.DashboardBaseUrl),
        };

        // If every field is null this would be a no-op override row — just clear it instead.
        if (ov.SmtpHost is null && ov.SmtpPort is null && ov.UseSsl is null &&
            ov.Username is null && ov.Password is null && ov.FromAddress is null &&
            ov.FromName is null && ov.DashboardBaseUrl is null)
        {
            await repo.ClearAsync(ct);
            return Ok(new { cleared = true });
        }

        await repo.SaveAsync(ov, ct);
        return await Get(ct);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await repo.ClearAsync(ct);
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] TestEmailRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RecipientEmail))
            return BadRequest("RecipientEmail is required.");

        var (success, message) = await notifier.SendTestEmailAsync(req.RecipientEmail.Trim(), ct);
        return Ok(new { success, message });
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record EmailSettingsResponse(
    string  SmtpHost,
    int     SmtpPort,
    bool    UseSsl,
    string  Username,
    bool    HasPassword,
    string  FromAddress,
    string  FromName,
    string  DashboardBaseUrl,
    bool    HasOverrides);

public record TestEmailRequest(string RecipientEmail);

public record UpdateEmailSettingsRequest(
    string? SmtpHost,
    int?    SmtpPort,
    bool?   UseSsl,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName,
    string? DashboardBaseUrl);
