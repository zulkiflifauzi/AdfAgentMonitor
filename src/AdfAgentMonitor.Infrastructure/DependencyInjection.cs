using Anthropic;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using AdfAgentMonitor.Core.Interfaces;
using AdfAgentMonitor.Infrastructure.Persistence;
using AdfAgentMonitor.Infrastructure.Services;
using AdfAgentMonitor.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AdfAgentMonitor.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers all Infrastructure services: EF Core, repositories, ADF, Graph, Anthropic,
    /// and the configuration sections that drive them.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---------------------------------------------------------------------------
        // Settings — bound from appsettings.json / environment variables
        // ---------------------------------------------------------------------------

        services.Configure<AdfSettings>(configuration.GetSection(AdfSettings.SectionName));
        services.Configure<TeamsSettings>(configuration.GetSection(TeamsSettings.SectionName));
        services.Configure<AnthropicSettings>(configuration.GetSection(AnthropicSettings.SectionName));
        services.Configure<HangfireSettings>(configuration.GetSection(HangfireSettings.SectionName));
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));

        // ---------------------------------------------------------------------------
        // Azure credential — shared by ArmClient (ADF) and GraphServiceClient (Teams).
        // Uses ClientSecretCredential when TenantId/ClientId/ClientSecret are all set
        // (CI/CD, non-managed-identity environments). Falls back to DefaultAzureCredential
        // which supports Managed Identity, Azure CLI, VS / VS Code login, and env vars.
        // ---------------------------------------------------------------------------

        services.AddSingleton<TokenCredential>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AdfSettings>>().Value;

            if (settings.HasServicePrincipalCredentials)
                return new ClientSecretCredential(
                    settings.TenantId,
                    settings.ClientId,
                    settings.ClientSecret);

            return new DefaultAzureCredential();
        });

        // ---------------------------------------------------------------------------
        // Azure Resource Manager client (used by AdfService)
        // ---------------------------------------------------------------------------

        services.AddSingleton(sp =>
            new ArmClient(sp.GetRequiredService<TokenCredential>()));

        // ---------------------------------------------------------------------------
        // EF Core — SQL Server
        // ---------------------------------------------------------------------------

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IPipelineRunStateRepository, PipelineRunStateRepository>();
        services.AddScoped<IAgentActivityLogRepository, AgentActivityLogRepository>();
        services.AddScoped<INotificationSettingsRepository, NotificationSettingsRepository>();

        // ---------------------------------------------------------------------------
        // Azure Data Factory service
        // ---------------------------------------------------------------------------

        services.AddScoped<IAdfService, AdfService>();

        // ---------------------------------------------------------------------------
        // Email notifications (MailKit SMTP)
        // ---------------------------------------------------------------------------

        services.AddScoped<IEmailNotifierService, EmailNotifierService>();

        // ---------------------------------------------------------------------------
        // Anthropic SDK → Semantic Kernel IChatCompletionService adapter
        // IAnthropicClient is thread-safe; use Singleton so the HTTP client is reused.
        // ---------------------------------------------------------------------------

        services.AddSingleton<IAnthropicClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AnthropicSettings>>().Value;
            return new AnthropicClient(new Anthropic.Core.ClientOptions
            {
                ApiKey = settings.ApiKey
            });
        });

        services.AddSingleton<IChatCompletionService, AnthropicChatCompletionService>();

        return services;
    }
}
