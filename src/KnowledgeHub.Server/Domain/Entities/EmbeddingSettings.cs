namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Single-row embedding/indexing provider override
/// (SPEC-20260926-settings-ux-embeddings RF-004): provider, endpoint, model,
/// dimensions and chunking knobs editable from /settings without redeploy.
/// The API key is NOT stored here — it lives in
/// <see cref="Settings.IIntegrationSecretStore"/> under the "embeddings" slug.
/// </summary>
public sealed class EmbeddingSettings
{
    /// <summary>Single-row table — the service always upserts row Id = 1.</summary>
    public int Id { get; set; }

    /// <summary>deterministic | ollama | openai | onnx</summary>
    public required string Provider { get; set; }

    /// <summary>Base URL for remote providers (ollama/openai); null for local ones.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model name sent to the provider (e.g. nomic-embed-text).</summary>
    public string? Model { get; set; }

    /// <summary>Expected vector length — changing it on an existing corpus
    /// requires reindex (old vectors keep their stamped dims).</summary>
    public int Dimensions { get; set; } = 384;

    /// <summary>Model directory for <c>Provider=onnx</c>.</summary>
    public string? ModelPath { get; set; }

    /// <summary>Chunking: max tokens per chunk (Ingestion:MaxTokens override).</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Chunking: overlap tail tokens (Ingestion:OverlapTokens override).</summary>
    public int? OverlapTokens { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
