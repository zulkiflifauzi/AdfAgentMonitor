namespace AdfAgentMonitor.Infrastructure.Settings;

/// <summary>
/// Bound from configuration section "Anthropic".
/// The API key must be supplied via environment variable or Azure Key Vault — never appsettings.json.
/// </summary>
public class AnthropicSettings
{
    public const string SectionName = "Anthropic";

    /// <summary>Anthropic API key. Inject via ANTHROPIC__APIKEY environment variable.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Model used for all chat completion calls from this service.</summary>
    public string ModelId { get; init; } = "claude-sonnet-4-20250514";

    /// <summary>
    /// Default temperature. Agents may override this per-call via
    /// <see cref="Microsoft.SemanticKernel.PromptExecutionSettings.ExtensionData"/>.
    /// </summary>
    public double Temperature { get; init; } = 0.0;

    /// <summary>
    /// Default max tokens. Agents may override this per-call via
    /// <see cref="Microsoft.SemanticKernel.PromptExecutionSettings.ExtensionData"/>.
    /// </summary>
    public int MaxTokens { get; init; } = 1024;
}
