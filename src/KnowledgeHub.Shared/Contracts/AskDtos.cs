namespace KnowledgeHub.Shared.Contracts;

/// <summary>POST /api/ask request (SPEC-20260914-llm-answer-synthesis RF-003).</summary>
public sealed record AskRequest
{
    public required string Question { get; init; }
    public int? TopK { get; init; }
    public Guid? SourceId { get; init; }
    /// <summary>hybrid | semantic | lexical — default semantic (same as /api/search).</summary>
    public string? Mode { get; init; }
    /// <summary>Override LLM generation; default true when a chat provider is configured.</summary>
    public bool? Generate { get; init; }
    /// <summary>Optional metadata filters (SPEC-20260923-retrieval-quality RF-003).</summary>
    public SearchFilter? Filters { get; init; }
}

/// <summary>One citation linking an answer marker [n] to a retrieved chunk.</summary>
public sealed record CitationDto
{
    public required int Index { get; init; }
    public required string Source { get; init; }
    public required string Title { get; init; }
    public required string Uri { get; init; }
    /// <summary>Vault-relative file path accepted by read_document; null for
    /// non-file-backed sources (SPEC-20260922-tool-descriptions-en-us RF-003).</summary>
    public string? Path { get; init; }
    public required double Score { get; init; }
}

/// <summary>Synthesized answer (or raw context when generation is off/unavailable).</summary>
public sealed record AskResponse
{
    public required string? Answer { get; init; }
    public required IReadOnlyList<CitationDto> Citations { get; init; }
    public required double LatencyMs { get; init; }
    /// <summary>Model that produced the answer; null when generated=false.</summary>
    public required string? Model { get; init; }
    /// <summary>True when the answer was synthesized by the configured chat provider.</summary>
    public required bool Generated { get; init; }
    /// <summary>Raw context used for the answer (when generated=false).</summary>
    public IReadOnlyList<SearchResultItem>? Context { get; init; }
}
