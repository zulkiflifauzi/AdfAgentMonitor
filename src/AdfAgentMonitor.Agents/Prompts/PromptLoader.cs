using System.Reflection;

namespace AdfAgentMonitor.Agents.Prompts;

/// <summary>
/// Loads two-section .prompty files embedded as assembly resources.
/// File format:
///   --- system ---
///   [system message content]
///   --- user ---
///   [user message template with {{Placeholder}} tokens]
/// </summary>
internal static class PromptLoader
{
    private const string SystemMarker = "--- system ---";
    private const string UserMarker   = "--- user ---";

    /// <summary>
    /// Reads the embedded resource <c>AdfAgentMonitor.Agents.Prompts.{name}.prompty</c>
    /// and returns the system prompt and user template as separate strings.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resource is not found or the file is missing the required section markers.
    /// </exception>
    internal static (string System, string UserTemplate) Load(string name)
    {
        var resourceName = $"AdfAgentMonitor.Agents.Prompts.{name}.prompty";

        using var stream = Assembly.GetExecutingAssembly()
                               .GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded prompt resource not found: '{resourceName}'. " +
                               $"Ensure the .prompty file is marked as EmbeddedResource in the project.");

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        var systemStart = content.IndexOf(SystemMarker, StringComparison.Ordinal);
        var userStart   = content.IndexOf(UserMarker,   StringComparison.Ordinal);

        if (systemStart < 0 || userStart < 0 || userStart <= systemStart)
            throw new InvalidOperationException(
                $"Prompt '{name}' must contain '--- system ---' followed by '--- user ---'.");

        var systemContent = content
            [(systemStart + SystemMarker.Length)..userStart]
            .Trim();

        var userContent = content
            [(userStart + UserMarker.Length)..]
            .Trim();

        return (systemContent, userContent);
    }
}
