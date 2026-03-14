using AdfAgentMonitor.Core.Enums;

namespace AdfAgentMonitor.Dashboard.Services;

public sealed class RunFilter
{
    public PipelineRunStatus? Status       { get; set; }
    public RemediationRisk?   Risk         { get; set; }
    public string?            PipelineName { get; set; }
    public DateTimeOffset?    DateFrom     { get; set; }
    public DateTimeOffset?    DateTo       { get; set; }

    public bool IsEmpty =>
        Status       is null &&
        Risk         is null &&
        string.IsNullOrEmpty(PipelineName) &&
        DateFrom     is null &&
        DateTo       is null;
}
