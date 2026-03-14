using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Models;

namespace AdfAgentMonitor.Core.Interfaces;

public interface IAgent
{
    Task<AgentResult> ExecuteAsync(PipelineRunState state, CancellationToken ct);
}
