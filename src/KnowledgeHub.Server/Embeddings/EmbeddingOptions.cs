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

    /// <summary>SPEC-20260924-asymmetric-embeddings: role-aware embeddings
    /// (query vs document). Enabling on an existing corpus requires reindex —
    /// document vectors produced without prefixes are not comparable.</summary>
    public AsymmetricOptions Asymmetric { get; set; } = new();

    /// <summary>OpenAI-compatible <c>input_type</c> sent with query embeddings
    /// (providers that support it, e.g. Voyage via compatible endpoint).</summary>
    public string? QueryInputType { get; set; }

    /// <summary>OpenAI-compatible <c>input_type</c> sent with document embeddings.</summary>
    public string? DocumentInputType { get; set; }

    public sealed class AsymmetricOptions
    {
        /// <summary>Master switch — default off so existing corpora keep working.</summary>
        public bool Enabled { get; set; }

        /// <summary>Derive prefixes from the model name (nomic/e5/bge) when the
        /// explicit prefixes are unset.</summary>
        public bool Auto { get; set; } = true;

        /// <summary>Explicit query prefix — wins over auto-detection.</summary>
        public string? QueryPrefix { get; set; }

        /// <summary>Explicit document prefix — wins over auto-detection.</summary>
        public string? DocumentPrefix { get; set; }
    }
}
