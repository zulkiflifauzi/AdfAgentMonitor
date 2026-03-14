using AdfAgentMonitor.Core.Enums;

namespace AdfAgentMonitor.Core.Models;

/// <summary>
/// Returned by every IAgent.ExecuteAsync implementation.
/// <para>
/// <see cref="NextStatus"/> tells the caller (or the orchestrating job) which
/// <see cref="PipelineRunStatus"/> the state row should be advanced to.
/// A failed result should still carry a meaningful <see cref="NextStatus"/>
/// (e.g. the same status to retry, or a terminal failure status).
/// </para>
/// </summary>
public record AgentResult(bool Success, string Message, PipelineRunStatus NextStatus);
