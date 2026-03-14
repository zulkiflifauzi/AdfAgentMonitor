using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Infrastructure.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AdfAgentMonitor.Infrastructure.Services;

public class EmailNotifierService(
    INotificationSettingsRepository settingsRepo,
    IEmailSettingsOverrideRepository overrideRepo,
    IOptions<EmailSettings> options,
    ILogger<EmailNotifierService> logger) : IEmailNotifierService
{
    private readonly EmailSettings _cfg = options.Value;

    private async Task<EmailSettings> GetEffectiveSettingsAsync(CancellationToken ct)
    {
        var ov = await overrideRepo.GetAsync(ct);
        if (ov is null) return _cfg;
        return new EmailSettings
        {
            SmtpHost         = ov.SmtpHost         ?? _cfg.SmtpHost,
            SmtpPort         = ov.SmtpPort         ?? _cfg.SmtpPort,
            UseSsl           = ov.UseSsl           ?? _cfg.UseSsl,
            Username         = ov.Username         ?? _cfg.Username,
            Password         = ov.Password         ?? _cfg.Password,
            FromAddress      = ov.FromAddress      ?? _cfg.FromAddress,
            FromName         = ov.FromName         ?? _cfg.FromName,
            DashboardBaseUrl = ov.DashboardBaseUrl ?? _cfg.DashboardBaseUrl,
        };
    }

    public async Task<bool> SendNotificationAsync(PipelineRunState state, CancellationToken ct = default)
    {
        try
        {
            var recipients = (await settingsRepo.GetAsync(ct)).RecipientEmailList;
            if (recipients.Count == 0)
            {
                logger.LogWarning("No notification recipients configured — skipping email for runId={RunId}.", state.PipelineRunId);
                return false;
            }

            var subject = state.Status switch
            {
                PipelineRunStatus.PendingApproval => $"[Action Required] ADF pipeline approval needed: {state.PipelineName}",
                PipelineRunStatus.Resolved        => $"[Resolved] ADF pipeline remediated: {state.PipelineName}",
                PipelineRunStatus.Failed          => $"[Alert] ADF pipeline failed: {state.PipelineName}",
                _                                 => $"[ADF Monitor] Pipeline status update: {state.PipelineName}"
            };

            var cfg  = await GetEffectiveSettingsAsync(ct);
            var body = BuildHtmlBody(state, cfg);
            await SendAsync(recipients, subject, body, cfg, ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification email for runId={RunId}.", state.PipelineRunId);
            return false;
        }
    }

    public async Task SendOutcomeEmailAsync(PipelineRunState state, string outcome, CancellationToken ct = default)
    {
        try
        {
            var recipients = (await settingsRepo.GetAsync(ct)).RecipientEmailList;
            if (recipients.Count == 0) return;

            var cfg     = await GetEffectiveSettingsAsync(ct);
            var subject = $"[ADF Monitor] Pipeline approval {outcome}: {state.PipelineName}";
            var body    = BuildOutcomeHtml(state, outcome);
            await SendAsync(recipients, subject, body, cfg, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send outcome email for runId={RunId}.", state.PipelineRunId);
        }
    }

    // -------------------------------------------------------------------------

    private async Task SendAsync(IEnumerable<string> to, string subject, string htmlBody, EmailSettings cfg, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
        foreach (var addr in to)
            message.To.Add(MailboxAddress.Parse(addr));
        message.Subject = subject;
        message.Body    = new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(cfg.SmtpHost, cfg.SmtpPort,
            cfg.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

        if (!string.IsNullOrEmpty(cfg.Username))
            await client.AuthenticateAsync(cfg.Username, cfg.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private string BuildHtmlBody(PipelineRunState state, EmailSettings cfg)
    {
        var statusColor = state.Status switch
        {
            PipelineRunStatus.PendingApproval => "#e65100",
            PipelineRunStatus.Resolved        => "#2e7d32",
            PipelineRunStatus.Failed          => "#c62828",
            _                                 => "#1565c0"
        };

        var statusLabel = state.Status switch
        {
            PipelineRunStatus.PendingApproval => "APPROVAL REQUIRED",
            PipelineRunStatus.Resolved        => "RESOLVED",
            PipelineRunStatus.Remediating     => "REMEDIATING",
            PipelineRunStatus.Failed          => "PIPELINE FAILED",
            _                                 => "STATUS UPDATE"
        };

        var sb = new System.Text.StringBuilder();
        sb.Append($"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
              <div style="background:{statusColor};color:#fff;padding:16px 24px;border-radius:4px 4px 0 0">
                <div style="font-size:12px;font-weight:bold;letter-spacing:1px">{statusLabel}</div>
                <div style="font-size:22px;font-weight:bold;margin-top:4px">{System.Net.WebUtility.HtmlEncode(state.PipelineName)}</div>
              </div>
              <div style="border:1px solid #e0e0e0;border-top:none;padding:24px;border-radius:0 0 4px 4px">
                <table style="width:100%;border-collapse:collapse;margin-bottom:16px">
                  <tr><td style="color:#757575;padding:4px 8px 4px 0;width:130px">Factory</td><td style="padding:4px 0">{System.Net.WebUtility.HtmlEncode(state.FactoryName)}</td></tr>
                  <tr><td style="color:#757575;padding:4px 8px 4px 0">Run ID</td><td style="padding:4px 0;font-family:monospace;font-size:12px">{state.PipelineRunId}</td></tr>
                  <tr><td style="color:#757575;padding:4px 8px 4px 0">Failed At</td><td style="padding:4px 0">{state.FailedAt?.ToString("f") ?? "—"}</td></tr>
                  <tr><td style="color:#757575;padding:4px 8px 4px 0">Diagnosis</td><td style="padding:4px 0">{state.DiagnosisCode?.ToString() ?? "—"}</td></tr>
                  <tr><td style="color:#757575;padding:4px 8px 4px 0">Risk Level</td><td style="padding:4px 0">{state.RemediationRisk?.ToString() ?? "—"}</td></tr>
                </table>
            """);

        if (!string.IsNullOrWhiteSpace(state.DiagnosisSummary))
            sb.Append($"<p><strong>Diagnosis Summary</strong><br>{System.Net.WebUtility.HtmlEncode(state.DiagnosisSummary)}</p>");

        if (!string.IsNullOrWhiteSpace(state.RemediationPlan))
            sb.Append($"<p><strong>Remediation Plan</strong><br>{System.Net.WebUtility.HtmlEncode(state.RemediationPlan)}</p>");

        if (state.Status == PipelineRunStatus.PendingApproval && !string.IsNullOrEmpty(cfg.DashboardBaseUrl))
        {
            var url = $"{cfg.DashboardBaseUrl.TrimEnd('/')}/approvals";
            sb.Append($"""
                <div style="margin-top:24px">
                  <a href="{url}" style="background:#1565c0;color:#fff;padding:10px 24px;border-radius:4px;text-decoration:none;font-weight:bold">
                    Open Approvals Dashboard
                  </a>
                </div>
                """);
        }

        sb.Append("</div></div>");
        return sb.ToString();
    }

    private string BuildOutcomeHtml(PipelineRunState state, string outcome)
    {
        var color = outcome.Equals("approved", StringComparison.OrdinalIgnoreCase) ? "#2e7d32" : "#c62828";
        return $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;border:1px solid #e0e0e0;border-radius:4px;padding:24px">
              <p>The approval request for pipeline <strong>{System.Net.WebUtility.HtmlEncode(state.PipelineName)}</strong>
              has been <span style="color:{color};font-weight:bold">{System.Net.WebUtility.HtmlEncode(outcome.ToUpperInvariant())}</span>.</p>
              <table style="width:100%;border-collapse:collapse">
                <tr><td style="color:#757575;padding:4px 8px 4px 0;width:130px">Run ID</td><td style="font-family:monospace;font-size:12px">{state.PipelineRunId}</td></tr>
                <tr><td style="color:#757575;padding:4px 8px 4px 0">Factory</td><td>{System.Net.WebUtility.HtmlEncode(state.FactoryName)}</td></tr>
              </table>
            </div>
            """;
    }
}
