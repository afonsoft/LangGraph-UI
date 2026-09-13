namespace KnowledgeHub.Server.Embeddings;

/// <summary>Configuration for <see cref="IEmbeddingProvider"/> selection (SPEC-03 RF-003).</summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>deterministic | ollama | openai</summary>
    public string Provider { get; set; } = "deterministic";

    /// <summary>Base URL — e.g. http://localhost:11434 (Ollama) or https://api.openai.com.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Bearer key for OpenAI-compatible providers. Never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model name sent to the provider (e.g. nomic-embed-text).</summary>
    public string? Model { get; set; }

    /// <summary>Expected vector length; provider output is validated/sliced to this.</summary>
    public int Dimensions { get; set; } = 384;
}
