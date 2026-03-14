using Microsoft.JSInterop;

namespace AdfAgentMonitor.Dashboard.Services;

/// <summary>
/// Polls /api/runs/summary every 30 seconds and fires a browser notification
/// when the pending-approval count increases.  Must be started explicitly via
/// <see cref="StartAsync"/> (called from App.razor after localStorage is loaded).
/// </summary>
public sealed class NotificationService(
    IMonitorApiClient  apiClient,
    IJSRuntime         js) : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private int  _lastPendingCount = -1;  // -1 = not yet polled

    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads notification preferences from localStorage, checks browser
    /// Notification permission, then starts the 30-second polling loop.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public async Task StartAsync()
    {
        if (_cts is not null) return;   // already running

        try
        {
            var enabled    = await js.InvokeAsync<string?>("localStorage.getItem", "adf:settings:notificationsEnabled");
            var notifyAppr = await js.InvokeAsync<string?>("localStorage.getItem", "adf:settings:notifyOnApproval");

            if (!bool.TryParse(enabled,    out var isEnabled)    || !isEnabled)    return;
            if (!bool.TryParse(notifyAppr, out var isApprEnabled) || !isApprEnabled) return;

            var permission = await js.InvokeAsync<string>("adfNotifications.getPermission");
            if (permission != "granted") return;
        }
        catch { return; }

        _cts = new CancellationTokenSource();
        _ = RunPollLoopAsync(_cts.Token);
    }

    // -------------------------------------------------------------------------

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await PollAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* disposed */ }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            var summary = await apiClient.GetSummaryAsync(ct);

            if (_lastPendingCount >= 0 && summary.PendingApproval > _lastPendingCount)
            {
                var newCount = summary.PendingApproval - _lastPendingCount;
                var body = newCount == 1
                    ? "1 pipeline run is awaiting your approval."
                    : $"{newCount} pipeline runs are awaiting your approval.";

                await js.InvokeVoidAsync(
                    "adfNotifications.showNotification",
                    "ADF Monitor — Pending Approval",
                    body,
                    ct);
            }

            _lastPendingCount = summary.PendingApproval;
        }
        catch { /* network errors are silently ignored */ }
    }

    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }
    }
}
