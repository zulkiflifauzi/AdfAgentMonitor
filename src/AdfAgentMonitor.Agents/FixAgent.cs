using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Enums;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace AdfAgentMonitor.Agents;

/// <summary>
/// Applies automated remediation for pipeline runs whose diagnosis is complete.
/// High-risk runs are always escalated to human approval. Low- and Medium-risk runs
/// are remediated automatically where a safe action exists; all others are escalated.
/// </summary>
/// <remarks>
/// Remediation routing by <see cref="DiagnosisCode"/>:
/// <list type="table">
///   <item><term>SinkThrottled</term><description>Retry pipeline re-run with exponential backoff (30 s → 60 s → 120 s).</description></item>
///   <item><term>IROffline</term><description>Initiate Integration Runtime start.</description></item>
///   <item><term>SourceUnreachable, CredentialExpired, DataValidationError, TransformationError, Unknown</term>
///         <description>Escalate to PendingApproval — human review required.</description></item>
/// </list>
/// </remarks>
public class FixAgent(
    IAdfService adfService,
    IPipelineRunStateRepository repository,
    ILogger<FixAgent> logger) : IAgent
{
    private const int RerunMaxAttempts      = 3;
    private const int RerunInitialDelaySeconds = 30;

    // ---------------------------------------------------------------------------
    // IAgent
    // ---------------------------------------------------------------------------

    public async Task<AgentResult> ExecuteAsync(PipelineRunState state, CancellationToken ct)
    {
        if (state.Status != PipelineRunStatus.Remediating)
        {
            logger.LogWarning(
                "FixAgent received a run in unexpected status {Status} (runId={RunId}). Skipping.",
                state.Status, state.PipelineRunId);
            return new AgentResult(
                Success: false,
                Message: $"Expected status {PipelineRunStatus.Remediating}, got {state.Status}.",
                NextStatus: state.Status);
        }

        logger.LogInformation(
            "FixAgent starting for pipeline {PipelineName} (runId={RunId}) " +
            "diagCode={DiagnosisCode} risk={Risk}.",
            state.PipelineName, state.PipelineRunId, state.DiagnosisCode, state.RemediationRisk);

        // ------------------------------------------------------------------
        // High risk → always require human approval regardless of diagnosis
        // ------------------------------------------------------------------

        if (state.RemediationRisk == RemediationRisk.High)
        {
            return await EscalateAsync(state,
                $"Remediation risk is {RemediationRisk.High} — human approval required before any action.",
                ct);
        }

        // ------------------------------------------------------------------
        // Low / Medium risk → route by diagnosis code
        // ------------------------------------------------------------------

        return state.DiagnosisCode switch
        {
            DiagnosisCode.SinkThrottled =>
                await RerunWithBackoffAsync(state, ct),

            DiagnosisCode.IROffline =>
                await RestartIrAsync(state, ct),

            // These codes always require human review regardless of risk level:
            // SourceUnreachable and CredentialExpired may involve credentials or
            // network config that should not be touched without explicit sign-off.
            // DataValidationError and TransformationError risk data corruption.
            // Unknown has insufficient information for safe automated action.
            DiagnosisCode.SourceUnreachable  or
            DiagnosisCode.CredentialExpired  or
            DiagnosisCode.DataValidationError or
            DiagnosisCode.TransformationError or
            DiagnosisCode.Unknown            or
            _ =>
                await EscalateAsync(state,
                    $"Diagnosis code {state.DiagnosisCode} requires human review " +
                    "regardless of risk level.",
                    ct)
        };
    }

    // ---------------------------------------------------------------------------
    // Remediation: SinkThrottled — retry with exponential backoff
    // ---------------------------------------------------------------------------

    private async Task<AgentResult> RerunWithBackoffAsync(PipelineRunState state, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= RerunMaxAttempts; attempt++)
        {
            // Delay doubles on each attempt: 30 s, 60 s, 120 s.
            var delaySeconds = RerunInitialDelaySeconds * (int)Math.Pow(2, attempt - 1);

            logger.LogInformation(
                "SinkThrottled: waiting {Delay}s before re-run attempt {Attempt}/{Max} " +
                "for runId={RunId}.",
                delaySeconds, attempt, RerunMaxAttempts, state.PipelineRunId);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);

            var newRunId = await adfService.TriggerPipelineRerunAsync(
                state.PipelineName, state.PipelineRunId, ct);

            if (newRunId is not null)
            {
                state.Status         = PipelineRunStatus.Resolved;
                state.ResolvedAt     = DateTimeOffset.UtcNow;
                state.RemediationPlan =
                    $"Pipeline re-triggered after {attempt} attempt(s) due to sink throttling. " +
                    $"New ADF run: {newRunId}.";

                await repository.UpdateAsync(state, ct);

                logger.LogInformation(
                    "FixAgent resolved runId={RunId} via re-run on attempt {Attempt}. " +
                    "NewRunId={NewRunId}.",
                    state.PipelineRunId, attempt, newRunId);

                return new AgentResult(
                    Success:    true,
                    Message:    $"Pipeline re-triggered (newRunId={newRunId}) after {attempt} attempt(s).",
                    NextStatus: PipelineRunStatus.Resolved);
            }

            logger.LogWarning(
                "Re-run attempt {Attempt}/{Max} failed for runId={RunId}.",
                attempt, RerunMaxAttempts, state.PipelineRunId);
        }

        return await EscalateAsync(state,
            $"All {RerunMaxAttempts} re-run attempts failed after exponential backoff. " +
            "Manual intervention required.",
            ct);
    }

    // ---------------------------------------------------------------------------
    // Remediation: IROffline — initiate Integration Runtime start
    // ---------------------------------------------------------------------------

    private async Task<AgentResult> RestartIrAsync(PipelineRunState state, CancellationToken ct)
    {
        var started = await adfService.RestartIntegrationRuntimeAsync(ct);

        if (started)
        {
            state.Status          = PipelineRunStatus.Resolved;
            state.ResolvedAt      = DateTimeOffset.UtcNow;
            state.RemediationPlan = "Integration Runtime start operation accepted. " +
                                    "The IR may take several minutes to reach Running state.";

            await repository.UpdateAsync(state, ct);

            logger.LogInformation(
                "FixAgent resolved runId={RunId}: IR start accepted.", state.PipelineRunId);

            return new AgentResult(
                Success:    true,
                Message:    "Integration Runtime start operation accepted.",
                NextStatus: PipelineRunStatus.Resolved);
        }

        return await EscalateAsync(state,
            "Integration Runtime restart failed — manual intervention required.",
            ct);
    }

    // ---------------------------------------------------------------------------
    // Escalation — sets status to PendingApproval and records reason
    // ---------------------------------------------------------------------------

    private async Task<AgentResult> EscalateAsync(
        PipelineRunState state,
        string reason,
        CancellationToken ct)
    {
        state.Status         = PipelineRunStatus.PendingApproval;
        state.ApprovalStatus = "Pending";

        await repository.UpdateAsync(state, ct);

        logger.LogInformation(
            "FixAgent escalated runId={RunId} to {Status}. Reason: {Reason}",
            state.PipelineRunId, PipelineRunStatus.PendingApproval, reason);

        return new AgentResult(
            Success:    true,
            Message:    reason,
            NextStatus: PipelineRunStatus.PendingApproval);
    }
}
