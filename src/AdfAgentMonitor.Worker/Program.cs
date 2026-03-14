using AdfAgentMonitor.Agents;
using AdfAgentMonitor.Infrastructure;
using AdfAgentMonitor.Worker;
using Hangfire;

var builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------------
// Infrastructure — EF Core, repositories, ADF, Graph, Anthropic, settings
// ---------------------------------------------------------------------------

builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------
// Hangfire — SQL Server storage + background processing server
// ---------------------------------------------------------------------------

// Hangfire:SqlConnectionString takes precedence; fall back to the shared EF Core
// connection string so local dev works with a single connection string entry.
var hangfireCs =
    builder.Configuration["Hangfire:SqlConnectionString"] is { Length: > 0 } hcs ? hcs
    : builder.Configuration.GetConnectionString("DefaultConnection")
      ?? throw new InvalidOperationException(
             "Set Hangfire:SqlConnectionString or ConnectionStrings:DefaultConnection. " +
             "Environment variable: HANGFIRE__SQLCONNECTIONSTRING or " +
             "CONNECTIONSTRINGS__DEFAULTCONNECTION.");

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(hangfireCs));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = builder.Configuration.GetValue("Hangfire:WorkerCount", defaultValue: 5);
});

// ---------------------------------------------------------------------------
// Agents — Scoped so each Hangfire job gets a fresh instance with its own
//          DbContext, decoupling state between concurrent job executions.
// ---------------------------------------------------------------------------

builder.Services.AddScoped<MonitorAgent>();
builder.Services.AddScoped<DiagnosticsAgent>();
builder.Services.AddScoped<FixAgent>();
builder.Services.AddScoped<NotifierAgent>();

// ---------------------------------------------------------------------------
// Orchestrator + recurring-job setup
// ---------------------------------------------------------------------------

builder.Services.AddScoped<PipelineMonitorOrchestrator>();
builder.Services.AddHostedService<HangfireSetupService>();

// ---------------------------------------------------------------------------

var host = builder.Build();
host.Run();
