namespace KnowledgeHub.Shared.Contracts;

/// <summary>Retrieval mode (SPEC-20260914-hybrid-retrieval RF-002).</summary>
public enum SearchMode
{
    /// <summary>Vector similarity fused with FTS5 lexical rank via RRF (k=60).</summary>
    Hybrid,
    /// <summary>Vector similarity only — the pre-hybrid behavior.</summary>
    Semantic,
    /// <summary>SQLite FTS5 lexical matching only.</summary>
    Lexical
}

/// <summary>Optional rank provenance for a fused result (debug/audit).</summary>
public sealed record SearchScoreBreakdown
{
    public int? VectorRank { get; init; }
    public int? LexicalRank { get; init; }
    public double Fused { get; init; }
}

/// <summary>One ranked chunk hit from semantic search (SPEC-02 RF-004).</summary>
public sealed record SearchResultItem
{
    public required string ChunkText { get; init; }
    public required string DocumentTitle { get; init; }
    public required string SourceName { get; init; }
    public required Guid SourceId { get; init; }
    public required double Score { get; init; }
    public required string UriReference { get; init; }
    /// <summary>Rank provenance when retrieved in hybrid/lexical mode; null for plain semantic.</summary>
    public SearchScoreBreakdown? ScoreBreakdown { get; init; }
}

public sealed record SearchResponse
{
    public required IReadOnlyList<SearchResultItem> Results { get; init; }
}
