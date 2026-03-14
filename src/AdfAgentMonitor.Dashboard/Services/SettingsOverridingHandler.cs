namespace AdfAgentMonitor.Dashboard.Services;

/// <summary>
/// DelegatingHandler that applies runtime API connection overrides from
/// <see cref="DashboardSettingsService"/> to every outgoing HTTP request.
/// When neither override is set the request passes through unchanged,
/// so the default <c>appsettings.json</c> configuration remains in effect.
/// </summary>
public sealed class SettingsOverridingHandler(DashboardSettingsService settings) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken  ct)
    {
        if (settings.HasBaseUrlOverride && request.RequestUri is { } originalUri)
        {
            // Preserve the original path + query and graft it onto the override base.
            var newBase = new Uri(settings.ApiBaseUrl!.TrimEnd('/') + "/");
            request.RequestUri = new Uri(newBase, originalUri.PathAndQuery.TrimStart('/'));
        }

        if (settings.HasApiKeyOverride)
        {
            request.Headers.Remove("X-Api-Key");
            request.Headers.Add("X-Api-Key", settings.ApiKey);
        }

        return base.SendAsync(request, ct);
    }
}
