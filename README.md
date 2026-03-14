# AdfAgentMonitor

A .NET 9 multi-agent system that monitors Azure Data Factory pipelines, diagnoses failures
with Claude (via Semantic Kernel), auto-remediates where safe, and routes human approvals
through a Blazor WebAssembly dashboard and Microsoft Teams Adaptive Cards.

---

## Architecture

```
Azure Data Factory
      │  failed runs (poll every 2 min)
      ▼
 MonitorAgent ──► PipelineRunState table (SQL Server)
      │
      ▼
 DiagnosticsAgent  (Claude claude-sonnet-4-20250514 via Anthropic API)
      │  classifies error → DiagnosisCode + RemediationRisk
      ▼
 FixAgent
  ├── Low/Medium risk + SinkThrottled  → retry pipeline (exponential backoff)
  ├── Low/Medium risk + IROffline      → restart Integration Runtime
  └── High risk or manual-review codes → PendingApproval
      │
      ▼
 NotifierAgent  (Microsoft Graph → Teams Adaptive Card)
      │
      ├── [Auto-resolved]  → "RESOLVED" card
      └── [PendingApproval] → card with Approve / Reject buttons
                                    │
                        ┌───────────┴───────────┐
                        ▼                       ▼
                   POST /api/approvals/{id}/approve   POST /api/approvals/{id}/reject
                        │                       │
                   FixAgent runs again       Status = Resolved
                   NotifierAgent posts       NotifierAgent posts
                   outcome card              rejection card

Dashboard (Blazor WASM PWA)
  ├── GET /api/runs, /api/runs/summary  (stat cards + run list)
  ├── GET /api/runs/{id}, /api/runs/{id}/logs
  ├── GET /api/activity                 (agent activity timeline)
  └── GET /api/health                   (connection test)
```

All agent-to-agent communication is via the shared `PipelineRunState` SQL table —
agents never call each other directly.

---

## Projects

| Project | Role |
|---|---|
| `AdfAgentMonitor.Core` | Entities, interfaces, enums, models — no dependencies |
| `AdfAgentMonitor.Infrastructure` | EF Core, ADF SDK, Graph SDK, Anthropic SDK, DI wiring |
| `AdfAgentMonitor.Agents` | All agent implementations + Hangfire orchestrator |
| `AdfAgentMonitor.Worker` | Hangfire processing host (recurring + background jobs) |
| `AdfAgentMonitor.Api` | ASP.NET Core API — approval webhooks + dashboard read endpoints |
| `AdfAgentMonitor.Dashboard` | Blazor WebAssembly PWA — monitoring UI + approval interface |

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- SQL Server 2019+ or [LocalDB](https://learn.microsoft.com/sql/database-engine/configure-windows/sql-server-express-localdb) (local dev)
- An Azure subscription with an Azure Data Factory instance
- An Azure AD application registration (or Managed Identity for production)
- An Anthropic API key
- A Microsoft 365 tenant with a Teams team and channel

---

## Configuration reference

Configuration follows the standard .NET layered model:
`appsettings.json` → `appsettings.{Environment}.json` → environment variables → user secrets.

**Environment variables use double-underscore (`__`) as the section separator.**

### Connection Strings

| Key | Env var | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `CONNECTIONSTRINGS__DEFAULTCONNECTION` | SQL Server connection string for EF Core. Hangfire falls back to this when `Hangfire:SqlConnectionString` is not set. |

### AzureDataFactory

| Key | Env var | Required | Description |
|---|---|---|---|
| `AzureDataFactory:SubscriptionId` | `AZUREDATAFACTORY__SUBSCRIPTIONID` | Yes | Azure subscription GUID |
| `AzureDataFactory:ResourceGroup` | `AZUREDATAFACTORY__RESOURCEGROUP` | Yes | Resource group containing the factory |
| `AzureDataFactory:FactoryName` | `AZUREDATAFACTORY__FACTORYNAME` | Yes | ADF instance name |
| `AzureDataFactory:TenantId` | `AZUREDATAFACTORY__TENANTID` | SP only | Azure AD tenant ID |
| `AzureDataFactory:ClientId` | `AZUREDATAFACTORY__CLIENTID` | SP only | Service principal app (client) ID |
| `AzureDataFactory:ClientSecret` | `AZUREDATAFACTORY__CLIENTSECRET` | SP only | Service principal client secret — use Key Vault or env var |
| `AzureDataFactory:LookbackMinutes` | `AZUREDATAFACTORY__LOOKBACKMINUTES` | No | How far back to scan for failures (default: `60`) |
| `AzureDataFactory:DefaultIntegrationRuntimeName` | `AZUREDATAFACTORY__DEFAULTINTEGRATIONRUNTIMENAME` | No | IR to restart on `IROffline` diagnosis; leave empty to always escalate |

**Credential selection:** When all three of `TenantId`, `ClientId`, and `ClientSecret` are
set, `ClientSecretCredential` is used. When any is absent, `DefaultAzureCredential` is used
(supports Managed Identity, `az login`, Visual Studio / VS Code sign-in, and the `AZURE_*`
environment variables).

The same credential is shared by both the ADF ARM client and the Microsoft Graph client.

### Teams

| Key | Env var | Description |
|---|---|---|
| `Teams:TeamId` | `TEAMS__TEAMID` | Azure AD object ID of the Teams team |
| `Teams:ChannelId` | `TEAMS__CHANNELID` | Channel ID within that team |
| `Teams:ApprovalWebhookBaseUrl` | `TEAMS__APPROVALWEBHOOKBASEURL` | Public base URL of the Api host (e.g. `https://adfmonitor.example.com`). Used to build the Approve/Reject button URLs in the Adaptive Card. |

### Anthropic

| Key | Env var | Description |
|---|---|---|
| `Anthropic:ApiKey` | `ANTHROPIC__APIKEY` | Anthropic API key — never commit to source control |
| `Anthropic:ModelId` | `ANTHROPIC__MODELID` | Model to use (default: `claude-sonnet-4-20250514`) |
| `Anthropic:Temperature` | `ANTHROPIC__TEMPERATURE` | Sampling temperature (default: `0.0`) |
| `Anthropic:MaxTokens` | `ANTHROPIC__MAXTOKENS` | Max tokens per response (default: `1024`) |

### Hangfire

| Key | Env var | Description |
|---|---|---|
| `Hangfire:SqlConnectionString` | `HANGFIRE__SQLCONNECTIONSTRING` | Dedicated connection string for Hangfire's job-store schema. Falls back to `ConnectionStrings:DefaultConnection` when empty. |
| `Hangfire:WorkerCount` | `HANGFIRE__WORKERCOUNT` | Background worker thread count in the Worker host (default: `5`) |

### Api

| Key | Env var | Description |
|---|---|---|
| `Api:ApiKey` | `API__APIKEY` | Secret value expected in the `X-Api-Key` request header on all `/api/*` endpoints. Never commit to source control. |
| `Cors:AllowedOrigins` | `CORS__ALLOWEDORIGINS__0`, `__1`, … | Array of origins allowed to call the Api from the browser (i.e. the Dashboard host URL). In `appsettings.Development.json` this defaults to the standard Blazor dev ports. |

### Dashboard

The Dashboard reads its API connection settings from `appsettings.json` (baked into the WASM bundle at publish time) and can be overridden at runtime via the **Settings → Connection** tab (values are persisted to `localStorage`).

| Key | Description |
|---|---|
| `ApiBaseUrl` | Base URL of the Api host, e.g. `https://localhost:7001`. Must end without a trailing slash. |
| `ApiKey` | Value sent as the `X-Api-Key` header on every request. |

---

## Local development setup

### 1. Clone and restore

```bash
git clone <repo-url>
cd AdfAgentMonitor
dotnet restore
```

### 2. Apply database migrations

Ensure LocalDB is running (it starts automatically on first use on Windows):

```bash
dotnet tool restore           # installs local dotnet-ef v9 from .config/dotnet-tools.json

dotnet tool run dotnet-ef database update \
  --project src/AdfAgentMonitor.Infrastructure \
  --startup-project src/AdfAgentMonitor.Infrastructure \
  --context AppDbContext
```

The LocalDB connection string in `appsettings.Development.json` is:
```
Server=(localdb)\mssqllocaldb;Database=AdfAgentMonitor;Trusted_Connection=True
```

### 3. Set secrets

Use `dotnet user-secrets` to keep sensitive values out of source control:

```bash
# Worker project
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "AzureDataFactory:SubscriptionId"  "<your-sub-id>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "AzureDataFactory:ResourceGroup"   "<your-rg>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "AzureDataFactory:FactoryName"     "<your-adf-name>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Teams:TeamId"                     "<teams-team-id>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Teams:ChannelId"                  "<teams-channel-id>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Teams:ApprovalWebhookBaseUrl"     "https://localhost:7001"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Anthropic:ApiKey"                 "<your-anthropic-key>"

# Api project
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Api:ApiKey"                       "<choose-a-random-secret>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Anthropic:ApiKey"                 "<your-anthropic-key>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Teams:TeamId"                     "<teams-team-id>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Teams:ChannelId"                  "<teams-channel-id>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Teams:ApprovalWebhookBaseUrl"     "https://localhost:7001"
```

For Azure authentication locally, `DefaultAzureCredential` will pick up your `az login`
session automatically — no need to set `TenantId`/`ClientId`/`ClientSecret` for local dev.

### 4. Configure the Dashboard

Add `ApiBaseUrl` and `ApiKey` to `src/AdfAgentMonitor.Dashboard/wwwroot/appsettings.Development.json`
(create it if it doesn't exist — it is gitignored):

```json
{
  "ApiBaseUrl": "https://localhost:7001",
  "ApiKey": "<same value you set for Api:ApiKey above>"
}
```

Alternatively, leave this file empty and set the values after launch via **Settings → Connection** in the UI — they are saved to `localStorage` and take effect immediately.

### 5. Run all three hosts

Open three terminals:

```bash
# Terminal 1 — background job processor
dotnet run --project src/AdfAgentMonitor.Worker

# Terminal 2 — approval webhook + dashboard API
dotnet run --project src/AdfAgentMonitor.Api

# Terminal 3 — Blazor WebAssembly dashboard
dotnet run --project src/AdfAgentMonitor.Dashboard
```

The Dashboard will be available at `https://localhost:7071` (or `http://localhost:5071`).
Check `src/AdfAgentMonitor.Dashboard/Properties/launchSettings.json` for the exact ports and
ensure they match the `Cors:AllowedOrigins` list in `appsettings.Development.json`.

---

## Production deployment

### Environment variables (container / App Service / AKS)

Set the following environment variables on each host. Use Azure Key Vault references
where supported to avoid storing secrets in plain text.

**Both Worker and Api:**

```
CONNECTIONSTRINGS__DEFAULTCONNECTION=Server=<sql>;Database=AdfAgentMonitor;...
AZUREDATAFACTORY__SUBSCRIPTIONID=<guid>
AZUREDATAFACTORY__RESOURCEGROUP=<name>
AZUREDATAFACTORY__FACTORYNAME=<name>
AZUREDATAFACTORY__TENANTID=<guid>          # omit to use Managed Identity
AZUREDATAFACTORY__CLIENTID=<guid>          # omit to use Managed Identity
AZUREDATAFACTORY__CLIENTSECRET=<secret>    # omit to use Managed Identity
TEAMS__TEAMID=<id>
TEAMS__CHANNELID=<id>
TEAMS__APPROVALWEBHOOKBASEURL=https://your-api-host.example.com
ANTHROPIC__APIKEY=<key>
HANGFIRE__SQLCONNECTIONSTRING=Server=<sql>;Database=AdfAgentMonitor;...
```

**Api only:**

```
API__APIKEY=<random-secret>
CORS__ALLOWEDORIGINS__0=https://your-dashboard-host.example.com
```

**Worker only:**

```
HANGFIRE__WORKERCOUNT=10
```

### Required Azure AD permissions

The service principal (or Managed Identity) needs:

| Resource | Permission | Type |
|---|---|---|
| Azure Data Factory | `Contributor` or `Data Factory Contributor` | Azure RBAC |
| Microsoft Graph | `ChannelMessage.Send` | Application permission |
| Microsoft Graph | `ChatMessage.ReadWrite` (for `UpdateCardOutcomeAsync`) | Application permission |

---

## Approval flow walkthrough

1. ADF pipeline fails → **MonitorAgent** inserts a `PipelineRunState` row with `Status = Failed`
2. **DiagnosticsAgent** calls Claude → sets `DiagnosisCode`, `DiagnosisSummary`, `RemediationRisk`
3. **FixAgent** routes the run:
   - Auto-remediated (SinkThrottled / IROffline, Low/Medium risk) → `Status = Resolved`
   - Everything else → `Status = PendingApproval`, `ApprovalStatus = Pending`
4. **NotifierAgent** posts an Adaptive Card to Teams
   - Resolved runs: informational card
   - PendingApproval runs: card with **Approve** and **Reject** buttons
5. A team member clicks **Approve** → Teams calls `POST /api/approvals/{id}/approve` with `X-Api-Key`
   - State → `Remediating`; FixAgent re-runs; outcome card posted
6. A team member clicks **Reject** → Teams calls `POST /api/approvals/{id}/reject`
   - State → `Resolved`; no remediation; rejection card posted

---

## Adding a new DiagnosisCode

1. Add the value to `Core/Enums/DiagnosisCode.cs`
2. Add a routing case in `Agents/FixAgent.cs`
3. Update the system prompt in `Agents/Prompts/DiagnosticsAgent.prompty`
4. Generate and apply a migration if a schema change is needed
