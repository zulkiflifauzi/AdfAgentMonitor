namespace AdfAgentMonitor.Dashboard.Services;

/// <summary>
/// App-level UI state shared across all components. Register as Scoped —
/// in Blazor WASM there is one DI container per browser tab, so Scoped == per-user-session.
/// </summary>
public sealed class LayoutState
{
    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public bool IsDarkMode { get; private set; } = true;

    /// <summary>UTC timestamp of the last successful API data fetch.</summary>
    public DateTime? LastRefreshed { get; private set; }

    public int PendingApprovalCount { get; private set; }

    /// <summary>False after an API call throws; true after a successful one.</summary>
    public bool IsApiConnected { get; private set; } = true;

    /// <summary>
    /// How often (in seconds) the layout refreshes the pending-approval count.
    /// Configurable from Settings → Display. Default: 30 s.
    /// </summary>
    public int RefreshIntervalSeconds { get; private set; } = 30;

    // -------------------------------------------------------------------------
    // Change notification — components subscribe to re-render on state change.
    // -------------------------------------------------------------------------

    public event Action? OnChange;

    // -------------------------------------------------------------------------
    // Mutations
    // -------------------------------------------------------------------------

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        NotifyStateChanged();
    }

    /// <summary>Sets dark mode to an explicit value (used when loading from localStorage).</summary>
    public void SetDarkMode(bool value)
    {
        if (IsDarkMode == value) return;
        IsDarkMode = value;
        NotifyStateChanged();
    }

    /// <summary>Updates the dashboard refresh interval (used when loading/saving from Settings).</summary>
    public void SetRefreshInterval(int seconds)
    {
        if (seconds <= 0) return;
        RefreshIntervalSeconds = seconds;
        NotifyStateChanged();
    }

    public void NotifyDataRefreshed()
    {
        LastRefreshed = DateTime.UtcNow;
        NotifyStateChanged();
    }

    public void SetPendingApprovalCount(int count)
    {
        PendingApprovalCount = count;
        NotifyStateChanged();
    }

    public void SetApiConnected(bool connected)
    {
        IsApiConnected = connected;
        NotifyStateChanged();
    }

    // -------------------------------------------------------------------------

    private void NotifyStateChanged() => OnChange?.Invoke();
}
