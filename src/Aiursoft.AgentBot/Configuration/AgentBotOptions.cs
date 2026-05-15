namespace Aiursoft.AgentBot.Configuration;

public enum AiEngine
{
    Gemini,
    Claude
}

/// <summary>
/// Configuration options for the Agent Bot.
/// </summary>
public class AgentBotOptions
{
    public string WorkspaceFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NugetNinjaWorkspace");

    public TimeSpan AiTimeout { get; set; } = TimeSpan.FromMinutes(35);

    public int ForkWaitDelayMs { get; set; } = 5000;

    /// <summary>
    /// The AI engine backend to use: "Gemini" or "Claude".
    /// </summary>
    public AiEngine Engine { get; set; } = AiEngine.Gemini;

    /// <summary>
    /// The AI model to use (passed to --model parameter).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// API key for the AI provider. Set as provider-specific env var (GEMINI_API_KEY, ANTHROPIC_API_KEY, etc.).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Custom API endpoint for the AI provider (e.g., "https://api.deepseek.com/anthropic" for Claude via DeepSeek).
    /// Only used when Engine is Claude. Sets ANTHROPIC_BASE_URL.
    /// </summary>
    public string? ApiEndpoint { get; set; }

    public bool LocalizationEnabled { get; set; } = false;

    /// <summary>
    /// The Ollama API endpoint for localization (e.g., "https://api.deepseek.com/chat/completions").
    /// </summary>
    public string? OllamaApiEndpoint { get; set; }

    /// <summary>
    /// The Ollama model to use for localization (e.g., "deepseek-chat").
    /// </summary>
    public string? OllamaModel { get; set; }

    /// <summary>
    /// The API key for Ollama API.
    /// </summary>
    public string? OllamaApiKey { get; set; }

    public int LocalizationConcurrentRequests { get; set; } = 8;

    public string[] LocalizationTargetLanguages { get; set; } = [];

    /// <summary>
    /// GitLab username of the reviewer to auto-assign on newly created MRs.
    /// Leave empty to skip reviewer assignment.
    /// </summary>
    public string? Reviewer { get; set; }
}
