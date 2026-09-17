namespace KnowledgeHub.Server.Embeddings;

/// <summary>Configuration for <see cref="IEmbeddingProvider"/> selection (SPEC-03 RF-003).</summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>deterministic | ollama | openai | onnx</summary>
    public string Provider { get; set; } = "deterministic";

    /// <summary>Base URL — e.g. http://localhost:11434 (Ollama) or https://api.openai.com.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Bearer key for OpenAI-compatible providers. Never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model name sent to the provider (e.g. nomic-embed-text).</summary>
    public string? Model { get; set; }

    /// <summary>Expected vector length; provider output is validated/sliced to this.</summary>
    public int Dimensions { get; set; } = 384;

    /// <summary>Directory holding model.onnx + vocab.txt for <c>Provider=onnx</c>
    /// (SPEC-20260917-onnx-local-embeddings RF-002). Relative paths resolve
    /// against the working directory; default <c>models/all-MiniLM-L6-v2</c>.</summary>
    public string? ModelPath { get; set; }
}
