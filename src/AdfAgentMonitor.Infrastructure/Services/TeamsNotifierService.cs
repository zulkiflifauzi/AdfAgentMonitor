using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System.Text.Json;

namespace AdfAgentMonitor.Infrastructure.Services;

public class TeamsNotifierService(
    GraphServiceClient graphClient,
    IOptions<TeamsSettings> options,
    ILogger<TeamsNotifierService> logger) : ITeamsNotifierService
{
    private readonly TeamsSettings _settings = options.Value;

    // ---------------------------------------------------------------------------
    // ITeamsNotifierService
    // ---------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<string?> SendNotificationAsync(
        PipelineRunState state,
        CancellationToken ct = default)
    {
        var card    = BuildAdaptiveCard(state);
        var message = WrapCardInMessage(card);

        try
        {
            var posted = await graphClient
                .Teams[_settings.TeamId]
                .Channels[_settings.ChannelId]
                .Messages
                .PostAsync(message, cancellationToken: ct);

            logger.LogInformation(
                "Teams card posted for run {PipelineRunId} (status={Status}). MessageId={MessageId}",
                state.PipelineRunId, state.Status, posted?.Id);

            return posted?.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to post Teams card for run {PipelineRunId} (status={Status}).",
                state.PipelineRunId, state.Status);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateCardOutcomeAsync(
        string messageId,
        string outcome,
        CancellationToken ct = default)
    {
        // Graph API does not support updating card attachments in-place.
        // We replace the message body with a plain text outcome summary.
        var updatedMessage = new ChatMessage
        {
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content     = $"<p>This pipeline approval request has been <strong>{outcome}</strong>.</p>"
            }
        };

        try
        {
            await graphClient
                .Teams[_settings.TeamId]
                .Channels[_settings.ChannelId]
                .Messages[messageId]
                .PatchAsync(updatedMessage, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not update Teams card body for message {MessageId}.", messageId);
        }
    }

    // ---------------------------------------------------------------------------
    // Adaptive Card builder
    // ---------------------------------------------------------------------------

    private object BuildAdaptiveCard(PipelineRunState state)
    {
        var (containerStyle, headerLabel) = state.Status switch
        {
            PipelineRunStatus.Resolved        => ("good",      "RESOLVED"),
            PipelineRunStatus.PendingApproval => ("attention", "APPROVAL REQUIRED"),
            PipelineRunStatus.Remediating     => ("warning",   "REMEDIATING"),
            PipelineRunStatus.Failed          => ("attention", "PIPELINE FAILED"),
            _                                 => ("emphasis",  "STATUS UPDATE")
        };

        var bodyItems = new List<object>
        {
            // Coloured header banner
            new
            {
                type  = "Container",
                style = containerStyle,
                items = new object[]
                {
                    new
                    {
                        type   = "TextBlock",
                        text   = headerLabel,
                        weight = "Bolder",
                        size   = "Small",
                        color  = "Light",
                        spacing = "None"
                    },
                    new
                    {
                        type    = "TextBlock",
                        text    = state.PipelineName,
                        weight  = "Bolder",
                        size    = "Large",
                        color   = "Light",
                        wrap    = true,
                        spacing = "None"
                    }
                }
            },

            // Core fact table
            new
            {
                type    = "FactSet",
                spacing = "Medium",
                facts   = BuildFacts(state)
            }
        };

        // Diagnosis summary (only when present)
        if (!string.IsNullOrWhiteSpace(state.DiagnosisSummary))
        {
            bodyItems.Add(new
            {
                type    = "TextBlock",
                text    = "Diagnosis Summary",
                weight  = "Bolder",
                spacing = "Medium"
            });
            bodyItems.Add(new
            {
                type    = "TextBlock",
                text    = state.DiagnosisSummary,
                wrap    = true,
                spacing = "None"
            });
        }

        // Remediation plan (only when present)
        if (!string.IsNullOrWhiteSpace(state.RemediationPlan))
        {
            bodyItems.Add(new
            {
                type    = "TextBlock",
                text    = "Remediation Plan",
                weight  = "Bolder",
                spacing = "Medium"
            });
            bodyItems.Add(new
            {
                type    = "TextBlock",
                text    = state.RemediationPlan,
                wrap    = true,
                spacing = "None"
            });
        }

        // Approval prompt (only when pending)
        if (state.Status == PipelineRunStatus.PendingApproval)
        {
            bodyItems.Add(new
            {
                type    = "TextBlock",
                text    = "Please review the diagnosis and remediation plan, then approve or reject the proposed action.",
                wrap    = true,
                spacing = "Medium",
                isSubtle = true
            });
        }

        // Build card root — actions are only present for PendingApproval
        var card = new Dictionary<string, object>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"]    = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"]    = bodyItems
        };

        if (state.Status == PipelineRunStatus.PendingApproval)
        {
            var approveUrl = $"{_settings.ApprovalWebhookBaseUrl}/api/approvals/{state.Id}/approve";
            var rejectUrl  = $"{_settings.ApprovalWebhookBaseUrl}/api/approvals/{state.Id}/reject";

            card["actions"] = new object[]
            {
                new
                {
                    type  = "Action.OpenUrl",
                    title = "Approve",
                    url   = approveUrl,
                    style = "positive"
                },
                new
                {
                    type  = "Action.OpenUrl",
                    title = "Reject",
                    url   = rejectUrl,
                    style = "destructive"
                }
            };
        }

        return card;
    }

    private static object[] BuildFacts(PipelineRunState state)
    {
        var facts = new List<object>
        {
            new { title = "Factory",    value = state.FactoryName },
            new { title = "Run ID",     value = state.PipelineRunId },
            new { title = "Status",     value = state.Status.ToString() },
            new { title = "Failed At",  value = state.FailedAt?.ToString("f") ?? "—" },
            new { title = "Diagnosis",  value = state.DiagnosisCode?.ToString() ?? "—" },
            new { title = "Risk Level", value = state.RemediationRisk?.ToString() ?? "—" }
        };

        if (!string.IsNullOrWhiteSpace(state.ApprovalStatus))
            facts.Add(new { title = "Approval",    value = state.ApprovalStatus });

        if (state.ResolvedAt.HasValue)
            facts.Add(new { title = "Resolved At", value = state.ResolvedAt.Value.ToString("f") });

        return [.. facts];
    }

    // ---------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------

    private static ChatMessage WrapCardInMessage(object card)
        => new()
        {
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content     = "<attachment id=\"adfCard\"></attachment>"
            },
            Attachments =
            [
                new ChatMessageAttachment
                {
                    Id          = "adfCard",
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Content     = JsonSerializer.Serialize(card)
                }
            ]
        };
}
