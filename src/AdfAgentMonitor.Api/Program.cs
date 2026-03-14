using System.Text.Json;
using System.Text.Json.Serialization;
using AdfAgentMonitor.Agents;
using AdfAgentMonitor.Api.Authentication;
using AdfAgentMonitor.Infrastructure;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// CORS — allows the Blazor PWA to call the API from its origin.
// Allowed origins are configured in appsettings.json under Cors:AllowedOrigins.
// Example env var: CORS__ALLOWEDORIGINS__0=https://myapp.azurestaticapps.net
// ---------------------------------------------------------------------------

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(opt =>
    opt.AddPolicy("BlazorPwa", policy =>
        policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST")
            .WithHeaders("Content-Type", "X-Api-Key")));

// ---------------------------------------------------------------------------
// Infrastructure — EF Core, repositories, ADF, Graph, Anthropic, settings
// ---------------------------------------------------------------------------

builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------
// Hangfire — SQL Server storage only (no processing server in the Api).
// The Api uses IBackgroundJobClient to enqueue jobs that the Worker processes.
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Agents + orchestrator — registered so Hangfire can strongly type job calls
// and so the controller can call orchestrator.EnqueueFixChain / EnqueueNotifier.
// ---------------------------------------------------------------------------

builder.Services.AddScoped<MonitorAgent>();
builder.Services.AddScoped<DiagnosticsAgent>();
builder.Services.AddScoped<FixAgent>();
builder.Services.AddScoped<NotifierAgent>();
builder.Services.AddScoped<PipelineMonitorOrchestrator>();

// ---------------------------------------------------------------------------
// API key authentication filter
// ---------------------------------------------------------------------------

builder.Services.Configure<ApiSettings>(
    builder.Configuration.GetSection(ApiSettings.SectionName));
builder.Services.AddScoped<ApiKeyAuthFilter>();

// ---------------------------------------------------------------------------
// MVC + OpenAPI
// Enums are serialised as their string names (e.g. "PendingApproval") so the
// Blazor dashboard can deserialise them with JsonStringEnumConverter.
// Null properties are omitted to keep responses compact.
// ---------------------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
        opt.JsonSerializerOptions.DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull;
        opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

// ---------------------------------------------------------------------------

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseCors("BlazorPwa");
app.MapControllers();

app.Run();
