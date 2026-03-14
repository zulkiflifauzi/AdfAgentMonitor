# AdfAgentMonitor

A .NET 9 multi-agent system that monitors Azure Data Factory pipelines, diagnoses failures
with Claude (via Semantic Kernel), auto-remediates where safe, and routes human approvals
through HTML email notifications and a Blazor WebAssembly dashboard.

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
 NotifierAgent  (MailKit → HTML email to all configured recipients)
      │
      ├── [Auto-resolved]  → "RESOLVED" email
      └── [PendingApproval] → email with link to /approvals dashboard
                                    │
                    Operator opens Dashboard → Approvals page
                        │
                        ┌───────────┴───────────┐
                        ▼                       ▼
                   POST /api/approvals/{id}/approve   POST /api/approvals/{id}/reject
                        │                       │
                   FixAgent runs again       Status = Resolved
                   NotifierAgent sends       NotifierAgent sends
                   outcome email             rejection email

Dashboard (Blazor WASM PWA)
  ├── GET /api/runs, /api/runs/summary        (stat cards + run list)
  ├── GET /api/runs/{id}, /api/runs/{id}/logs
  ├── GET /api/activity                       (agent activity timeline)
  ├── GET /api/health                         (connection test)
  ├── GET|PUT /api/settings/notifications     (recipient list)
  ├── GET|PUT|DELETE /api/settings/email      (SMTP overrides)
  └── POST /api/settings/email/test           (send test email)
```

All agent-to-agent communication is via the shared `PipelineRunState` SQL table —
agents never call each other directly.

---

## Projects

| Project | Role |
|---|---|
| `AdfAgentMonitor.Core` | Entities, interfaces, enums, models — no dependencies |
| `AdfAgentMonitor.Infrastructure` | EF Core, ADF SDK, MailKit, Anthropic SDK, DI wiring |
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
- An SMTP server (any provider — Gmail, SendGrid, SES, or a local relay such as [MailHog](https://github.com/mailhog/MailHog) for dev)

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

The credential is used by the ADF ARM client only.

### Email

| Key | Env var | Required | Description |
|---|---|---|---|
| `Email:SmtpHost` | `EMAIL__SMTPHOST` | Yes | SMTP server hostname |
| `Email:SmtpPort` | `EMAIL__SMTPPORT` | No | SMTP port (default: `587`) |
| `Email:UseSsl` | `EMAIL__USESSL` | No | Use STARTTLS (default: `true`) |
| `Email:Username` | `EMAIL__USERNAME` | No | SMTP auth username — omit for unauthenticated relays |
| `Email:Password` | `EMAIL__PASSWORD` | No | SMTP auth password — use Key Vault or env var |
| `Email:FromAddress` | `EMAIL__FROMADDRESS` | Yes | Sender email address |
| `Email:FromName` | `EMAIL__FROMNAME` | No | Sender display name (default: `ADF Agent Monitor`) |
| `Email:DashboardBaseUrl` | `EMAIL__DASHBOARDBASEURL` | No | Base URL of the Dashboard (e.g. `https://dashboard.example.com`). Used to build the "Open Approvals" link in emails. |

Notification **recipients** are not in config — they are stored in the database and managed via **Settings → Notifications** in the Dashboard (or `PUT /api/settings/notifications`). Multiple recipients are supported; all receive the same email in a single send.

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

### DataProtection

The SMTP password entered in **Settings → Email** is encrypted at rest using ASP.NET Core Data Protection (AES-256-CBC + HMACSHA256) before being stored in the database. The Worker and Api must share the same key ring to encrypt/decrypt the same value.

| Key | Env var | Description |
|---|---|---|
| `DataProtection:KeysPath` | `DATAPROTECTION__KEYSPATH` | Optional path to a shared directory where Data Protection keys are persisted. When omitted, keys use the platform default (`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` on Windows). **Both Worker and Api must point to the same path in production.** |

> **Production note:** For multi-machine or containerised deployments configure a shared key store — e.g. Azure Blob Storage (`PersistKeysToAzureBlobStorage`) or SQL Server (`PersistKeysToDbContext`). See the [Data Protection docs](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview) for options.

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
  "Anthropic:ApiKey"                 "<your-anthropic-key>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Email:SmtpHost"                   "<your-smtp-host>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Email:Username"                   "<your-smtp-username>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Email:Password"                   "<your-smtp-password>"
dotnet user-secrets --project src/AdfAgentMonitor.Worker set \
  "Email:FromAddress"                "<noreply@yourdomain.com>"

# Api project
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Api:ApiKey"                       "<choose-a-random-secret>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Anthropic:ApiKey"                 "<your-anthropic-key>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Email:SmtpHost"                   "<your-smtp-host>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Email:Username"                   "<your-smtp-username>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Email:Password"                   "<your-smtp-password>"
dotnet user-secrets --project src/AdfAgentMonitor.Api set \
  "Email:FromAddress"                "<noreply@yourdomain.com>"
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

**Option A — single command (recommended)**

```powershell
.\dev-start.ps1
```

This opens Worker, Api, and Dashboard each in a separate PowerShell window with the correct
`--urls` already set. Press **Enter** in the orchestrator window to stop all three.

**Option B — manual (three terminals)**

```bash
# Terminal 1 — background job processor
dotnet run --project src/AdfAgentMonitor.Worker

# Terminal 2 — approval webhook + dashboard API
dotnet run --project src/AdfAgentMonitor.Api --urls "https://localhost:7059;http://localhost:5070"

# Terminal 3 — Blazor WebAssembly dashboard
dotnet run --project src/AdfAgentMonitor.Dashboard --urls "https://localhost:7071;http://localhost:5071"
```

The Dashboard will be available at `https://localhost:7071` (or `http://localhost:5071`).
Ensure these ports are listed in the `Cors:AllowedOrigins` array in
`src/AdfAgentMonitor.Api/appsettings.Development.json`.

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
EMAIL__SMTPHOST=smtp.example.com
EMAIL__SMTPPORT=587
EMAIL__USESSL=true
EMAIL__USERNAME=<smtp-username>
EMAIL__PASSWORD=<smtp-password>
EMAIL__FROMADDRESS=adfmonitor@example.com
EMAIL__DASHBOARDBASEURL=https://your-dashboard-host.example.com
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

---

## Approval flow walkthrough

1. ADF pipeline fails → **MonitorAgent** inserts a `PipelineRunState` row with `Status = Failed`
2. **DiagnosticsAgent** calls Claude → sets `DiagnosisCode`, `DiagnosisSummary`, `RemediationRisk`
3. **FixAgent** routes the run:
   - Auto-remediated (SinkThrottled / IROffline, Low/Medium risk) → `Status = Resolved`
   - Everything else → `Status = PendingApproval`, `ApprovalStatus = Pending`
4. **NotifierAgent** sends an HTML email to all configured recipients
   - Resolved runs: informational "auto-resolved" email
   - PendingApproval runs: email with a link to the `/approvals` page on the Dashboard
5. An operator opens the Dashboard → Approvals page and clicks **Approve**
   - `POST /api/approvals/{id}/approve` → State → `Remediating`; FixAgent re-runs; outcome email sent
6. An operator clicks **Reject**
   - `POST /api/approvals/{id}/reject` → State → `Resolved`; no remediation; rejection outcome email sent

---

## Dashboard settings tabs

| Tab | What it controls |
|---|---|
| **Connection** | API base URL and API key overrides (saved to `localStorage`); Test Connection button |
| **Notifications** | Email recipient list (multiple addresses); browser notification permissions and per-event toggles |
| **Email** | SMTP overrides stored in the database — host, port, STARTTLS, auth credentials, from address, dashboard base URL; **Send Test Email** to verify configuration without redeploying |
| **Display** | Dark mode toggle; dashboard refresh interval |

> **Email tab — Test Email:** Enter any address and click **Send Test**. The test uses the currently *saved* effective settings (appsettings + any DB overrides). Save your changes first before testing a new configuration.

---

## Adding a new DiagnosisCode

1. Add the value to `Core/Enums/DiagnosisCode.cs`
2. Add a routing case in `Agents/FixAgent.cs`
3. Update the system prompt in `Agents/Prompts/DiagnosticsAgent.prompty`
4. Generate and apply a migration if a schema change is needed
