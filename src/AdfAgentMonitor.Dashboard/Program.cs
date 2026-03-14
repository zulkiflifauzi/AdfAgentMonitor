using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using AdfAgentMonitor.Dashboard;
using AdfAgentMonitor.Dashboard.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// App-level UI state (dark mode, last-refreshed timestamp, pending approval count).
builder.Services.AddScoped<LayoutState>();

// Runtime-mutable API connection overrides populated from localStorage by App.razor.
// SettingsOverridingHandler reads from this service on every request so changes
// take effect immediately (same session) as well as after reload.
builder.Services.AddScoped<DashboardSettingsService>();
builder.Services.AddTransient<SettingsOverridingHandler>();

// Browser notification polling — started explicitly by App.razor after localStorage loads.
builder.Services.AddScoped<NotificationService>();

// Bind ApiClientSettings from root configuration keys (ApiBaseUrl, ApiKey).
builder.Services.Configure<ApiClientSettings>(builder.Configuration);

// Typed HttpClient: base address and X-Api-Key default header are set once here
// so MonitorApiClient itself stays free of configuration concerns.
// SettingsOverridingHandler sits in front and can override both per-request.
builder.Services.AddHttpClient<IMonitorApiClient, MonitorApiClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ApiClientSettings>>().Value;

    if (!string.IsNullOrEmpty(settings.ApiBaseUrl))
        client.BaseAddress = new Uri(settings.ApiBaseUrl);

    if (!string.IsNullOrEmpty(settings.ApiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
})
.AddHttpMessageHandler<SettingsOverridingHandler>();

await builder.Build().RunAsync();
