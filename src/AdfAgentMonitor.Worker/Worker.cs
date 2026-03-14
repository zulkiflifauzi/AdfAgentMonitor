using AdfAgentMonitor.Agents;
using Hangfire;

namespace AdfAgentMonitor.Worker;

/// <summary>
/// Registers Hangfire recurring jobs when the host starts.
/// The Hangfire background processing server itself is registered via
/// <c>services.AddHangfireServer()</c> in Program.cs as a separate hosted service.
/// </summary>
internal sealed class HangfireSetupService(IRecurringJobManager recurringJobs) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurringJobs.AddOrUpdate<PipelineMonitorOrchestrator>(
            recurringJobId: "monitor-adf-pipelines",
            methodCall:     o => o.MonitorAsync(CancellationToken.None),
            cronExpression: "*/2 * * * *");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
