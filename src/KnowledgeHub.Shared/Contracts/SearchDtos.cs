namespace KnowledgeHub.Shared.Contracts;

/// <summary>One ranked chunk hit from semantic search (SPEC-02 RF-004).</summary>
public sealed record SearchResultItem
{
    public required string ChunkText { get; init; }
    public required string DocumentTitle { get; init; }
    public required string SourceName { get; init; }
    public required Guid SourceId { get; init; }
    public required double Score { get; init; }
    public required string UriReference { get; init; }
}

public sealed record SearchResponse
{
    public required IReadOnlyList<SearchResultItem> Results { get; init; }
}
