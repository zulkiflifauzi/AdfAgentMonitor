# AdfAgentMonitor

## Purpose

AdfAgentMonitor is a .NET 9 multi-agent system that continuously monitors Azure Data Factory (ADF) pipeline runs, automatically diagnoses failures using AI reasoning (Semantic Kernel), proposes and — where safe — executes remediations, and keeps human operators informed and in control through Microsoft Teams Adaptive Card approval workflows.

The system is designed around a **shared-state agent model**: agents do not call each other directly. Instead, each agent reads from and writes to a central `PipelineRunState` table in SQL Server. A Hangfire scheduler drives each agent on its own cadence. This decoupling means any agent can be paused, redeployed, or replaced without affecting the others.

---

## Agent Pipeline

```
┌─────────────────────────────────────────────────────────────────────┐
│                         SQL Server                                  │
│                      PipelineRunState                               │
│  ┌──────────┐   ┌──────────────┐   ┌────────────┐   ┌──────────┐  │
│  │ Detected │──▶│  Diagnosing  │──▶│ Remediating│──▶│ Approved │  │
│  └──────────┘   └──────────────┘   └────────────┘   └──────────┘  │
└───────▲────────────────▲─────────────────▲────────────────▲────────┘
        │                │                 │                │
  MonitorAgent    DiagnosticsAgent      FixAgent      NotifierAgent
  (polls ADF)    (SK reasoning)      (SK + Azure)    (Teams cards)
                                           │
                                    ┌──────▼──────┐
                                    │ HumanApproval│
                                    │  (Teams AC) │
                                    └─────────────┘
```

### Agent Responsibilities

| Agent | Trigger | Reads state | Writes state | External calls |
|---|---|---|---|---|
| **MonitorAgent** | Hangfire recurring (e.g. every 2 min) | — | Creates `PipelineRunState` rows for newly failed runs | ADF Management SDK |
| **DiagnosticsAgent** | Hangfire recurring, filters `Status = Detected` | `PipelineRunState`, ADF run logs | Updates state to `Diagnosing` → `Diagnosed`; writes `DiagnosisResult` JSON | ADF SDK (log fetch), Semantic Kernel |
| **FixAgent** | Hangfire recurring, filters `Status = Approved` OR auto-safe runs | `PipelineRunState`, `DiagnosisResult` | Updates state to `Remediating` → `Remediated` or `Failed`; writes `RemediationLog` | ADF SDK (rerun/cancel), Azure SDK |
| **NotifierAgent** | Hangfire recurring, filters `Status = Diagnosed` | `PipelineRunState`, `DiagnosisResult` | Updates state to `PendingApproval`; writes `ApprovalRequestId` | Microsoft Graph API (Teams Adaptive Cards) |

### State Machine

```
Detected → Diagnosing → Diagnosed → PendingApproval → Approved → Remediating → Remediated
                                                      → Rejected → Closed
                                  → AutoRemediated (skips approval for low-risk fixes)
                        → DiagnosisFailed → Closed
```

Each transition is written as a single EF Core transaction that also appends a row to the `AuditLog` table, giving a full, immutable history of every state change.

---

## Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 9 |
| AI / Agent reasoning | Semantic Kernel (`Microsoft.SemanticKernel`) |
| Background scheduling | Hangfire (`Hangfire.AspNetCore`, `Hangfire.SqlServer`) |
| Persistence | SQL Server via Entity Framework Core 9 |
| Azure integration | `Azure.ResourceManager.DataFactory`, `Azure.Identity` |
| Teams notifications | Microsoft Graph SDK (`Microsoft.Graph`) |
| In-process messaging | MediatR (`MediatR`) |
| HTTP API | ASP.NET Core 9 minimal APIs |
| Error handling | `Result<T>` pattern (no exceptions for control flow) |
| Configuration | `Microsoft.Extensions.Options` + Azure App Config / env vars |
| Testing | xUnit, Moq, Testcontainers (SQL Server) |

---

## Solution Layout

```
AdfAgentMonitor.sln
└── src/
    ├── AdfAgentMonitor.Core            # Shared kernel — no external NuGet deps
    ├── AdfAgentMonitor.Agents          # Agent implementations (Semantic Kernel)
    ├── AdfAgentMonitor.Infrastructure  # EF Core, ADF SDK, Graph API, Hangfire storage
    ├── AdfAgentMonitor.Worker          # Hangfire host + job registrations only
    ├── AdfAgentMonitor.Api             # Webhook receiver + dashboard read endpoints
    └── AdfAgentMonitor.Dashboard       # Blazor WebAssembly PWA — monitoring UI + approval interface
```

### Dependency Graph

```
Core   ◄── Agents
Core   ◄── Infrastructure
Core   ◄── Worker     ──► Agents, Infrastructure
Core   ◄── Api        ──► Agents, Infrastructure
Core   ◄── Dashboard
```

Infrastructure never references Agents. Worker and Api are thin hosts — no business logic lives in either.
Dashboard is a standalone Blazor WebAssembly app — it references only Core for shared models and communicates with the backend exclusively through the Api HTTP endpoints.

---

## Project Details

### AdfAgentMonitor.Core

The shared kernel. Zero external NuGet dependencies. Everything else depends on this; it depends on nothing.

**Models (records, all immutable)**
- `PipelineRunState` — the central shared-state entity; owns the status field and all FK references
- `DiagnosisResult` — structured output from DiagnosticsAgent (cause category, confidence, affected datasets, root cause narrative)
- `RemediationProposal` — a ranked list of candidate fixes with risk levels
- `ApprovalRequest` / `ApprovalOutcome` — Teams card correlation identifiers and results
- `AuditEntry` — append-only log row (who/what/when/old state/new state)

**Interfaces**
- `IAgentJob` — marker interface implemented by every Hangfire job class
- `IPipelineRunStateRepository` — CRUD + state-transition queries
- `IAuditRepository` — append-only write
- `IAdfService` — ADF run queries and rerun/cancel commands
- `INotificationService` — send / update Teams Adaptive Cards
- `IApprovalStore` — read pending approvals by correlation ID

**Enums**
- `RunStateStatus` — the full state machine enum (Detected, Diagnosing, …)
- `RemediationRiskLevel` — Low / Medium / High (Low = eligible for auto-remediation)
- `DiagnosisCategory` — ConnectionFailure, CredentialExpiry, ResourceThrottle, DataQuality, Unknown, …

**Result type**
```csharp
// All agent methods and infrastructure calls return this — never throw for expected failures
public readonly record struct Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }

    public static Result<T> Ok(T value) => ...;
    public static Result<T> Fail(string error) => ...;
}
```

**MediatR notifications (in-process events)**
- `RunStateTransitionedNotification` — published after every state change; used for audit logging and optional side effects without coupling the state machine to downstream concerns

---

### AdfAgentMonitor.Agents

All AI reasoning lives here. Each agent is a class that:
1. Queries `IPipelineRunStateRepository` to find rows in the relevant status.
2. Performs its work (calling Semantic Kernel or Azure SDK via injected interfaces).
3. Writes results and advances state via `IPipelineRunStateRepository` in a single transaction.
4. Returns `Result<T>` — no exceptions bubble out.

**MonitorAgent**
- Calls `IAdfService.GetFailedRunsSinceAsync(lastPollTime)`.
- Inserts new `PipelineRunState` rows with `Status = Detected` for runs not already tracked.
- Idempotent — duplicate run IDs are ignored (upsert semantics).

**DiagnosticsAgent**
- Fetches ADF run logs via `IAdfService`.
- Builds a Semantic Kernel prompt with: pipeline name, error message, log tail, recent run history.
- Uses SK's structured output (JSON mode) to produce a `DiagnosisResult`.
- Advances state `Detected → Diagnosed` (or `DiagnosisFailed` on SK error).

**NotifierAgent**
- For each `Status = Diagnosed` row, composes a Teams Adaptive Card containing the diagnosis summary and two actions: Approve / Reject.
- Sends via `INotificationService` (Microsoft Graph).
- Persists the returned `activityId` as `ApprovalRequestId` and advances state to `PendingApproval`.
- Skips notification for `RemediationRiskLevel = Low` — marks directly as `AutoApproved` for FixAgent.

**FixAgent**
- Processes rows with `Status = Approved` or `Status = AutoApproved`.
- Reads the `RemediationProposal` from the state row.
- Executes the highest-ranked proposal via `IAdfService` (rerun, parameter patch, credential refresh, etc.).
- Advances state to `Remediated` or `RemediationFailed`.

**SK Kernel Configuration**
- Kernel is registered as a singleton in DI, configured in `Infrastructure`.
- All agents receive `Kernel` via constructor injection.
- Prompt templates live in `src/AdfAgentMonitor.Agents/Prompts/` as `.prompty` files, not inline strings.
- Function calling / structured output is used wherever the agent needs a typed response.

---

### AdfAgentMonitor.Infrastructure

Adapters for every external system. Implements all interfaces from Core. No business logic.

**Persistence — EF Core**
- `AdfMonitorDbContext` — single DbContext; owns `PipelineRunStates`, `AuditLog`, `ApprovalRequests` DbSets.
- `PipelineRunStateRepository` — implements `IPipelineRunStateRepository`; all state transitions are wrapped in `IDbContextTransaction` + publish `RunStateTransitionedNotification` via MediatR.
- `AuditRepository` — append-only; never updates rows.
- Migrations live in `Infrastructure/Migrations/`.

**ADF Integration**
- `AdfService` — wraps `DataFactoryManagementClient` from `Azure.ResourceManager.DataFactory`; authenticated via `DefaultAzureCredential`.
- Exposes: `GetFailedRunsSinceAsync`, `GetRunLogsAsync`, `TriggerRunAsync`, `CancelRunAsync`.

**Teams / Graph Integration**
- `TeamsNotificationService` — wraps `GraphServiceClient`; builds Adaptive Card JSON, posts to a configured Teams channel, stores `activityId`.
- `ApprovalStore` — reads `ApprovalRequests` by correlation ID; used by the Api webhook to resolve incoming approvals.

**Hangfire Storage**
- Hangfire SQL Server storage is configured here and injected into Worker.

**SK Kernel Registration**
- Extension method `AddSemanticKernel(this IServiceCollection)` wires up the Azure OpenAI or OpenAI backend, token limits, and retry pipeline.

---

### AdfAgentMonitor.Worker

The long-running background host. Contains **only** DI composition, Hangfire job registration, and `Program.cs`. No business logic.

```
Worker/
├── Program.cs               # AddDbContext, AddAgents, AddInfrastructure, Hangfire config
└── Jobs/
    ├── MonitorJob.cs        # IRecurringJob → calls MonitorAgent.ExecuteAsync()
    ├── DiagnosticsJob.cs
    ├── NotifierJob.cs
    └── FixJob.cs
```

Each Job class is a thin shell:

```csharp
public class MonitorJob(MonitorAgent agent) : IAgentJob
{
    public async Task ExecuteAsync() => await agent.ExecuteAsync();
}
```

Hangfire schedules are configured in `Program.cs` via `IRecurringJobManager` using cron expressions from `appsettings.json`.

**Rule:** If you find yourself writing an `if` statement in a Job class, the logic belongs in the agent, not the job.

---

### AdfAgentMonitor.Api

ASP.NET Core 9 minimal API. Contains **only** endpoint mappings, request/response DTOs, and DI wiring. No business logic.

**Endpoints**

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/approvals/{id}/approve` | Approve a run; advances state to `Approved` |
| `POST` | `/api/approvals/{id}/reject` | Reject a run with a reason; advances state to `Rejected` |
| `GET` | `/api/runs` | Filtered + paged list of `PipelineRunState` rows |
| `GET` | `/api/runs/summary` | Stat-card counts: totalToday, failedToday, remediatedToday, pendingApproval |
| `GET` | `/api/runs/{id}` | Full detail for a single run (404 if not found) |
| `GET` | `/api/runs/{id}/logs` | Raw ADF activity log text (404 if not found) |
| `GET` | `/api/activity` | Paged `AgentActivityLog` entries |
| `GET` | `/api/health` | Returns `{ status: "ok", timestamp }` |

**Rule:** Endpoints call repository methods or MediatR command handlers directly. They never instantiate agents or contain business logic.

---

### AdfAgentMonitor.Dashboard

Blazor WebAssembly standalone PWA. The human-facing monitoring and approval interface.

**Tech:**
- SDK: `Microsoft.NET.Sdk.BlazorWebAssembly`
- UI: MudBlazor v7 (`MudThemeProvider`, `MudDialogProvider`, `MudSnackbarProvider` in `App.razor`)
- PWA offline support via `service-worker.js` (dev) / `service-worker.published.js` (prod)

**Layout:**
```
Dashboard/
├── Program.cs               # WebAssemblyHostBuilder, service registrations
├── App.razor                # Router + MudProviders; loads localStorage settings on init
├── _Imports.razor           # Global @using statements
├── Layout/
│   ├── MainLayout.razor     # AppBar, Drawer, OfflineBanner, tick loop
│   └── NavMenu.razor        # MudNavMenu links with pending-approval badge
├── Pages/
│   ├── Home.razor           # Dashboard stat cards + recent runs  (@page "/")
│   ├── Runs.razor           # Filterable run list                 (@page "/runs")
│   ├── Approvals.razor      # Pending approval queue              (@page "/approvals")
│   ├── AgentActivity.razor  # Agent activity timeline + live mode (@page "/activity")
│   └── Settings.razor       # Connection / Notifications / Display (@page "/settings")
├── Components/
│   ├── PipelineRunDetailPanel.razor  # Slide-out details panel
│   ├── ApprovalCard.razor            # Single approval action card
│   └── OfflineBanner.razor           # Warning banner when navigator.onLine = false
├── Services/
│   ├── IMonitorApiClient.cs          # Interface + ActivityPage / ConnectionTestResult records
│   ├── MonitorApiClient.cs           # HttpClient-based implementation
│   ├── LayoutState.cs                # Scoped app-wide UI state (dark mode, refresh interval, etc.)
│   ├── DashboardSettingsService.cs   # Runtime-mutable API URL + key overrides (populated from localStorage)
│   ├── SettingsOverridingHandler.cs  # DelegatingHandler — applies URL/key overrides per-request
│   └── NotificationService.cs        # Polls /api/runs/summary every 30 s; fires browser notifications
└── wwwroot/
    ├── index.html                    # JS helpers: adfNotifications, adfOffline; SW registration
    ├── manifest.json                 # PWA manifest
    ├── service-worker.js             # Dev: network-first for /api/, pass-through for static assets
    ├── service-worker.published.js   # Prod: cache-first static + network-first /api/ with 5s timeout
    └── css/app.css                   # Global styles, loading spinner, activity slide-in animation
```

**Routes:**

| Path | Page | Description |
|---|---|---|
| `/` | `Home.razor` | Stat cards (total / failed / remediated / pending), recent runs list |
| `/runs` | `Runs.razor` | Full filterable/sortable run list |
| `/approvals` | `Approvals.razor` | Pending-approval queue with Approve / Reject actions |
| `/activity` | `AgentActivity.razor` | Agent activity timeline; live-mode auto-prepends new entries |
| `/settings` | `Settings.razor` | Connection, Notifications, and Display settings |

**Key Services:**

| Service | Lifetime | Purpose |
|---|---|---|
| `IMonitorApiClient` / `MonitorApiClient` | Scoped (via `AddHttpClient`) | All HTTP calls to the Api backend |
| `LayoutState` | Scoped | Dark mode, refresh interval, last-refreshed timestamp, pending count, API connectivity |
| `DashboardSettingsService` | Scoped | In-memory API URL + key overrides; populated from `localStorage` at startup |
| `SettingsOverridingHandler` | Transient | `DelegatingHandler` that applies `DashboardSettingsService` overrides on every request |
| `NotificationService` | Scoped | Browser notification polling; started by `App.razor.OnInitializedAsync` |

**localStorage key scheme:**

| Key | Type | Description |
|---|---|---|
| `adf:settings:apiBaseUrl` | string | Override API base URL |
| `adf:settings:apiKey` | string | Override API key |
| `adf:settings:darkMode` | bool | Dark mode preference |
| `adf:settings:refreshInterval` | int | Dashboard refresh interval in seconds |
| `adf:settings:notificationsEnabled` | bool | Master browser notification switch |
| `adf:settings:notifyOnFailure` | bool | Notify on new pipeline failure |
| `adf:settings:notifyOnApproval` | bool | Notify when pending-approval count increases |
| `adf:settings:notifyOnRemediation` | bool | Notify on auto-remediation |

**Rules:**
- Dashboard never talks to SQL or Hangfire directly — only through Api HTTP calls.
- No business logic in Blazor pages; all data fetching is via `IMonitorApiClient`.
- Pages are thin: they render data and dispatch user actions via the API client.

---

### API Contract (Dashboard ↔ Api)

All endpoints require the `X-Api-Key` header. Responses use camelCase JSON with string-serialised enums and `null` fields omitted.

| Method | Path | Auth | Query params | Response |
|---|---|---|---|---|
| `GET` | `/api/runs` | X-Api-Key | `status`, `risk`, `name`, `fromDate`, `toDate`, `page`, `pageSize` | `PipelineRunState[]` |
| `GET` | `/api/runs/summary` | X-Api-Key | — | `{ totalToday, failedToday, remediatedToday, pendingApproval }` |
| `GET` | `/api/runs/{id}` | X-Api-Key | — | `PipelineRunState` (404 if not found) |
| `GET` | `/api/runs/{id}/logs` | X-Api-Key | — | raw log text (404 if not found) |
| `POST` | `/api/approvals/{id}/approve` | X-Api-Key | — | 200 OK |
| `POST` | `/api/approvals/{id}/reject` | X-Api-Key | body: `{ "reason": "…" }` | 200 OK |
| `GET` | `/api/activity` | X-Api-Key | `agentName`, `success`, `from`, `to`, `page`, `pageSize` | `{ items, totalCount, page, pageSize }` |
| `GET` | `/api/health` | X-Api-Key | — | `{ status: "ok", timestamp }` |

**CORS:** The Api reads allowed origins from `Cors:AllowedOrigins` in `appsettings.json`. Development defaults: `http://localhost:5071`, `https://localhost:7071`, `http://localhost:5000`, `https://localhost:7000`.

---

## Coding Conventions

### Result\<T\> — No Exceptions for Control Flow

All methods that can fail in expected ways return `Result<T>`. Only truly unexpected conditions (infrastructure outages, programming errors) may throw.

```csharp
// Good
Result<DiagnosisResult> result = await diagnosticsAgent.DiagnoseAsync(runId, ct);
if (!result.IsSuccess)
{
    logger.LogWarning("Diagnosis failed for {RunId}: {Error}", runId, result.Error);
    return;
}

// Bad — do not throw ApplicationException or custom domain exceptions for expected failures
throw new DiagnosisFailedException("...");
```

### MediatR — In-Process Messaging

Use MediatR for:
- Decoupled side effects after state transitions (`INotificationHandler<RunStateTransitionedNotification>`).
- Command dispatch from the Api layer (`IRequest<Result<T>>`).
- Do **not** use MediatR as a service locator or to replace direct constructor injection within a single layer.

### No Business Logic in Worker or Api

- **Worker jobs** call exactly one agent method and return. Period.
- **Api endpoints** call exactly one MediatR command/query handler and map the result to an HTTP response. Period.
- If you need to add a conditional or loop in a job or endpoint, create a new method in the appropriate agent or handler instead.

### EF Core State Transitions

Every state transition must:
1. Load the entity and verify the current status is the expected predecessor (guard against race conditions).
2. Write the new status.
3. Append an `AuditEntry`.
4. Commit in a single transaction.
5. Publish `RunStateTransitionedNotification` **after** the transaction commits (use an outbox or post-commit hook, not inside the transaction).

### Semantic Kernel Prompts

- All prompts live in `.prompty` files under `src/AdfAgentMonitor.Agents/Prompts/`.
- No prompt strings are hardcoded inside C# methods.
- Every SK call specifies `MaxTokens` and a `Temperature` appropriate for the task (diagnosis = low temperature; narrative summaries = slightly higher).
- SK structured output (JSON schema enforcement) is mandatory for any agent that writes a typed result to the database.

### Dependency Injection Lifetimes

| Type | Lifetime |
|---|---|
| `AdfMonitorDbContext` | Scoped |
| Agent classes | Scoped (they take DbContext) |
| `GraphServiceClient` | Singleton |
| `Kernel` (Semantic Kernel) | Singleton |
| Hangfire job classes | Transient (Hangfire resolves per execution) |

### Configuration

All secrets (connection strings, API keys, tenant IDs) come from environment variables or Azure Key Vault. No secrets in `appsettings.json`. Use `IOptions<T>` for strongly-typed settings — never inject `IConfiguration` directly into agent or infrastructure classes.

---

## Testing Strategy

- **Unit tests** — agents are tested with mocked `IAdfService`, `INotificationService`, and a real in-memory or Sqlite EF Core context.
- **Integration tests** — use Testcontainers (`Testcontainers.MsSql`) to spin up a real SQL Server instance; test full agent → DB → state transition flows.
- **No Hangfire in tests** — jobs are thin shells; test the agents directly.
- **SK mocking** — use SK's `MockKernelBuilder` or substitute a deterministic `IChatCompletionService` in tests; never hit a live LLM endpoint in CI.
