namespace KnowledgeHub.Server.Chat;

/// <summary>
/// Configuration for the answer-generation chat client (SPEC-20260914-llm-answer-synthesis RF-001).
/// <c>Provider=none</c> (default) disables generation and preserves the legacy raw-context behavior.
/// </summary>
public sealed class ChatProviderOptions
{
    public const string SectionName = "Chat";

    /// <summary>none | ollama | openai</summary>
    public string Provider { get; set; } = "none";

    /// <summary>Base URL — e.g. http://localhost:11434 (Ollama) or https://api.openai.com.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Bearer key for OpenAI-compatible providers. Env var only — never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model name sent to the provider (e.g. llama3.1, gpt-4o-mini).</summary>
    public string? Model { get; set; }

    /// <summary>Optional sampling temperature forwarded to the provider.</summary>
    public double? Temperature { get; set; }

    /// <summary>Optional cap on generated tokens.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Per-request LLM timeout.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
