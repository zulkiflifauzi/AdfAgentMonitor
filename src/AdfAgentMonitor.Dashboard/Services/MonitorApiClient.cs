using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdfAgentMonitor.Core.Entities;
using AdfAgentMonitor.Core.Models;

namespace AdfAgentMonitor.Dashboard.Services;

public class MonitorApiClient(HttpClient http) : IMonitorApiClient
{
    // Case-insensitive to tolerate ASP.NET Core camelCase vs our PascalCase properties.
    // JsonStringEnumConverter handles enums serialised as strings by the API.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<List<PipelineRunState>> GetAllRunsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/runs", ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<List<PipelineRunState>>(JsonOptions, ct))!;
    }

    public async Task<List<PipelineRunState>> GetFilteredRunsAsync(RunFilter filter, CancellationToken ct = default)
    {
        var url = BuildRunsUrl(filter);
        var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<List<PipelineRunState>>(JsonOptions, ct))!;
    }

    private static string BuildRunsUrl(RunFilter filter)
    {
        if (filter.IsEmpty) return "api/runs";

        var q = new List<string>(5);

        if (filter.Status.HasValue)
            q.Add($"status={filter.Status.Value}");

        if (filter.Risk.HasValue)
            q.Add($"risk={filter.Risk.Value}");

        // Query param name aligns with GET /api/runs contract (name, fromDate, toDate).
        if (!string.IsNullOrEmpty(filter.PipelineName))
            q.Add($"name={Uri.EscapeDataString(filter.PipelineName)}");

        if (filter.DateFrom.HasValue)
            q.Add($"fromDate={Uri.EscapeDataString(filter.DateFrom.Value.ToString("O"))}");

        if (filter.DateTo.HasValue)
            q.Add($"toDate={Uri.EscapeDataString(filter.DateTo.Value.ToString("O"))}");

        return $"api/runs?{string.Join("&", q)}";
    }

    public async Task<List<PipelineRunState>> GetPendingApprovalsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/runs?status=PendingApproval", ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<List<PipelineRunState>>(JsonOptions, ct))!;
    }

    public async Task<PipelineRunState> GetRunByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/runs/{id}", ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PipelineRunState>(JsonOptions, ct))!;
    }

    public async Task ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/approvals/{id}/approve", content: null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task RejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"api/approvals/{id}/reject",
            new { reason },
            JsonOptions,
            ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<string?> GetRunLogsAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"api/runs/{id}/logs", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public async Task<RunSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/runs/summary", ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<RunSummary>(JsonOptions, ct))!;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetAsync("api/health", ct);
            return response.IsSuccessStatusCode
                ? new ConnectionTestResult(true,  $"Connected — HTTP {(int)response.StatusCode} {response.ReasonPhrase}")
                : new ConnectionTestResult(false, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task<ActivityPage> GetActivityAsync(
        string?         agentName = null,
        bool?           success   = null,
        DateTimeOffset? from      = null,
        DateTimeOffset? to        = null,
        int             page      = 1,
        int             pageSize  = 50,
        CancellationToken ct      = default)
    {
        var q = new List<string>(6);

        if (!string.IsNullOrWhiteSpace(agentName))
            q.Add($"agentName={Uri.EscapeDataString(agentName)}");

        if (success.HasValue)
            q.Add($"success={success.Value.ToString().ToLowerInvariant()}");

        if (from.HasValue)
            q.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");

        if (to.HasValue)
            q.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");

        q.Add($"page={page}");
        q.Add($"pageSize={pageSize}");

        var url      = $"api/activity?{string.Join("&", q)}";
        var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ActivityPage>(JsonOptions, ct))!;
    }

    public async Task<List<string>> GetNotificationRecipientsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/settings/notifications", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        await EnsureSuccessAsync(response, ct);
        var obj = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        if (obj.TryGetProperty("recipientEmails", out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Array)
            return [..prop.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)];
        return [];
    }

    public async Task SetNotificationRecipientsAsync(List<string> emails, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            "api/settings/notifications",
            new { RecipientEmails = emails },
            JsonOptions,
            ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<EmailSettingsDto?> GetEmailSettingsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/settings/email", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<EmailSettingsDto>(JsonOptions, ct);
    }

    public async Task<EmailSettingsDto?> SetEmailSettingsAsync(EmailSettingsRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync("api/settings/email", request, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
        // PUT may return { cleared: true } when all fields are null — handle gracefully.
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        if (json.TryGetProperty("cleared", out _)) return null;
        return json.Deserialize<EmailSettingsDto>(JsonOptions);
    }

    public async Task ClearEmailSettingsAsync(CancellationToken ct = default)
    {
        var response = await http.DeleteAsync("api/settings/email", ct);
        await EnsureSuccessAsync(response, ct);
    }

    // -------------------------------------------------------------------------

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            body = string.Empty;
        }

        var message = string.IsNullOrWhiteSpace(body)
            ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
            : body;

        throw new ApiException((int)response.StatusCode, message);
    }
}
