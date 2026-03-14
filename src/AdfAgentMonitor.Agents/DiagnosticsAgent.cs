using System.Text.Json;
using AdfAgentMonitor.Agents.Prompts;
using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AdfAgentMonitor.Agents;

/// <summary>
/// Fetches ADF activity run logs for a failed pipeline run, submits them to Claude
/// via Semantic Kernel, and classifies the failure into a <see cref="DiagnosisCode"/>
/// with an accompanying summary and <see cref="RemediationRisk"/> assessment.
/// </summary>
public class DiagnosticsAgent(
    IAdfService adfService,
    IPipelineRunStateRepository repository,
    IChatCompletionService chatService,
    ILogger<DiagnosticsAgent> logger) : IAgent
{
    // Prompt is loaded once per process; PromptLoader throws at startup if the
    // embedded resource is missing, making misconfiguration fail fast.
    private static readonly (string System, string UserTemplate) Prompt =
        PromptLoader.Load("DiagnosticsAgent");

    // DTO for deserialising the structured JSON response from the model.
    private sealed record DiagnosisResponse(string Code, string Summary, string Risk);

    // ---------------------------------------------------------------------------
    // IAgent
    // ---------------------------------------------------------------------------

    public async Task<AgentResult> ExecuteAsync(PipelineRunState state, CancellationToken ct)
    {
        if (state.Status != PipelineRunStatus.Failed)
        {
            logger.LogWarning(
                "DiagnosticsAgent received a run in unexpected status {Status} (runId={RunId}). Skipping.",
                state.Status, state.PipelineRunId);
            return new AgentResult(
                Success: false,
                Message: $"Expected status {PipelineRunStatus.Failed}, got {state.Status}.",
                NextStatus: state.Status);
        }

        logger.LogInformation(
            "DiagnosticsAgent starting for pipeline {PipelineName} (runId={RunId}).",
            state.PipelineName, state.PipelineRunId);

        // ------------------------------------------------------------------
        // 1. Fetch activity run logs from ADF
        // ------------------------------------------------------------------

        var logs = await adfService.GetActivityRunLogsAsync(state.PipelineRunId, ct);

        if (string.IsNullOrWhiteSpace(logs))
        {
            logger.LogWarning(
                "No activity run logs available for runId={RunId}. Proceeding with empty log context.",
                state.PipelineRunId);
            logs = "(No activity run logs available.)";
        }

        // ------------------------------------------------------------------
        // 2. Build chat history from the prompt template
        // ------------------------------------------------------------------

        var userMessage = Prompt.UserTemplate
            .Replace("{{PipelineName}}",  state.PipelineName)
            .Replace("{{FactoryName}}",   state.FactoryName)
            .Replace("{{PipelineRunId}}", state.PipelineRunId)
            .Replace("{{FailedAt}}",      state.FailedAt?.ToString("O") ?? "unknown")
            .Replace("{{ActivityLogs}}",  logs);

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(Prompt.System);
        chatHistory.AddUserMessage(userMessage);

        // Temperature = 0 for deterministic classification.
        // max_tokens = 256: the JSON response is small; a hard cap prevents runaway output.
        // Both are passed via ExtensionData so the agent stays decoupled from any
        // provider-specific settings class (e.g. OpenAIPromptExecutionSettings).
        var settings = new PromptExecutionSettings
        {
            ModelId = "claude-sonnet-4-20250514",
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = 0.0,
                ["max_tokens"]  = 256
            }
        };

        // ------------------------------------------------------------------
        // 3. Call the model
        // ------------------------------------------------------------------

        ChatMessageContent response;
        try
        {
            response = await chatService.GetChatMessageContentAsync(
                chatHistory, settings, kernel: null, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LLM call failed for runId={RunId}.", state.PipelineRunId);
            return new AgentResult(
                Success: false,
                Message: $"LLM call failed: {ex.Message}",
                NextStatus: PipelineRunStatus.Failed);
        }

        // ------------------------------------------------------------------
        // 4. Parse the structured JSON response
        // ------------------------------------------------------------------

        var parsed = ParseResponse(response.Content);

        var diagCode = Enum.TryParse<DiagnosisCode>(parsed.Code, ignoreCase: true, out var code)
            ? code
            : DiagnosisCode.Unknown;

        // Default to High on parse failure — safer to require human review than
        // to silently auto-remediate an incorrectly classified failure.
        var risk = Enum.TryParse<RemediationRisk>(parsed.Risk, ignoreCase: true, out var r)
            ? r
            : RemediationRisk.High;

        // ------------------------------------------------------------------
        // 5. Persist and advance state
        // ------------------------------------------------------------------

        state.DiagnosisCode    = diagCode;
        state.DiagnosisSummary = parsed.Summary;
        state.RemediationRisk  = risk;
        state.Status           = PipelineRunStatus.Remediating;

        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "DiagnosticsAgent completed for runId={RunId}. Code={Code} Risk={Risk} Summary={Summary}",
            state.PipelineRunId, diagCode, risk, parsed.Summary);

        return new AgentResult(
            Success: true,
            Message: $"Diagnosed as {diagCode} (risk: {risk}). {parsed.Summary}",
            NextStatus: PipelineRunStatus.Remediating);
    }

    // ---------------------------------------------------------------------------
    // JSON parsing — never throws; returns safe defaults on malformed output
    // ---------------------------------------------------------------------------

    private DiagnosisResponse ParseResponse(string? rawContent)
    {
        const string fallbackSummary = "Diagnosis could not be parsed from the model response.";

        if (string.IsNullOrWhiteSpace(rawContent))
            return new DiagnosisResponse("Unknown", fallbackSummary, "High");

        // Strip markdown fences in case the model ignored the no-fence instruction.
        var json = rawContent.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence    = json.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }

        // If multiple JSON objects are present, take the first complete one.
        var braceEnd = json.IndexOf('}');
        if (braceEnd >= 0)
            json = json[..(braceEnd + 1)];

        try
        {
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var parsedCode    = GetString(root, "code")    ?? "Unknown";
            var parsedSummary = GetString(root, "summary") ?? fallbackSummary;
            var parsedRisk    = GetString(root, "risk")    ?? "High";

            return new DiagnosisResponse(parsedCode, parsedSummary, parsedRisk);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "Could not parse model JSON response. Raw content: {Content}", rawContent);
            return new DiagnosisResponse("Unknown", fallbackSummary, "High");
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var el) ? el.GetString() : null;
}
