using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using AdfAgentMonitor.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AdfAgentMonitor.Infrastructure.Services;

/// <summary>
/// Implements Semantic Kernel's <see cref="IChatCompletionService"/> by delegating to the
/// official Anthropic .NET SDK. Registered as a singleton in the DI container.
/// <para>
/// No official Microsoft SK connector for Anthropic exists; this thin adapter
/// keeps the Agents project fully decoupled from the Anthropic SDK.
/// </para>
/// </summary>
public sealed class AnthropicChatCompletionService : IChatCompletionService
{
    private readonly IAnthropicClient _client;
    private readonly AnthropicSettings _settings;
    private readonly ILogger<AnthropicChatCompletionService> _logger;

    // IChatCompletionService inherits IAIService which requires Attributes.
    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?>();

    public AnthropicChatCompletionService(
        IAnthropicClient client,
        IOptions<AnthropicSettings> options,
        ILogger<AnthropicChatCompletionService> logger)
    {
        _client   = client;
        _settings = options.Value;
        _logger   = logger;
    }

    // ---------------------------------------------------------------------------
    // IChatCompletionService — non-streaming (the only path used by agents)
    // ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        // Resolve per-call overrides from ExtensionData, falling back to settings defaults.
        var modelId     = executionSettings?.ModelId ?? _settings.ModelId;
        var temperature = ReadDouble(executionSettings, "temperature") ?? _settings.Temperature;
        var maxTokens   = ReadInt(executionSettings,    "max_tokens")  ?? _settings.MaxTokens;

        // Extract system message(s) — Anthropic separates these from the message array.
        var systemText = string.Join("\n\n", chatHistory
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t)));

        // Build the messages array (user + assistant turns only).
        var messages = chatHistory
            .Where(m => m.Role != AuthorRole.System)
            .Select(m => new MessageParam
            {
                Role    = m.Role == AuthorRole.User ? Role.User : Role.Assistant,
                Content = new MessageParamContent(m.Content ?? string.Empty, null)
            })
            .ToList<MessageParam>();

        if (messages.Count == 0)
            throw new InvalidOperationException(
                "ChatHistory must contain at least one non-system message.");

        var requestParams = new MessageCreateParams
        {
            Model       = modelId,   // string → ApiEnum<string, Model> via implicit operator
            MaxTokens   = maxTokens,
            Messages    = messages,
            Temperature = temperature,
            System      = string.IsNullOrEmpty(systemText)
                ? null
                : new MessageCreateParamsSystem(systemText, null)
        };

        _logger.LogDebug(
            "Calling Anthropic API. Model={Model} MaxTokens={MaxTokens} Temperature={Temperature} Messages={Count}",
            modelId, maxTokens, temperature, messages.Count);

        var response = await _client.Messages.Create(requestParams, cancellationToken);

        // Concatenate all text blocks (non-text blocks are ignored for this use case).
        var text = string.Concat(response.Content
            .Where(b => b.TryPickText(out _))
            .Select(b =>
            {
                b.TryPickText(out var tb);
                return tb?.Text ?? string.Empty;
            }));

        return
        [
            new ChatMessageContent(AuthorRole.Assistant, text)
            {
                ModelId = response.Model
            }
        ];
    }

    // ---------------------------------------------------------------------------
    // IChatCompletionService — streaming (not used; agents call non-streaming)
    // ---------------------------------------------------------------------------

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"{nameof(AnthropicChatCompletionService)} does not support streaming. " +
            "Use GetChatMessageContentsAsync instead.");

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static double? ReadDouble(PromptExecutionSettings? settings, string key)
    {
        if (settings?.ExtensionData is null) return null;
        if (!settings.ExtensionData.TryGetValue(key, out var raw)) return null;
        return raw switch
        {
            double d  => d,
            float  f  => f,
            int    i  => i,
            long   l  => l,
            string s when double.TryParse(s, out var p) => p,
            _          => null
        };
    }

    private static int? ReadInt(PromptExecutionSettings? settings, string key)
    {
        if (settings?.ExtensionData is null) return null;
        if (!settings.ExtensionData.TryGetValue(key, out var raw)) return null;
        return raw switch
        {
            int    i => i,
            long   l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var p) => p,
            _         => null
        };
    }
}
