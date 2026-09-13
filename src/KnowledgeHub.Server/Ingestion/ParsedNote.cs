namespace KnowledgeHub.Server.Ingestion;

/// <summary>Result of parsing a Markdown note (SPEC-03 RF-001).</summary>
public sealed record ParsedNote
{
    public required string Title { get; init; }
    /// <summary>Body with the frontmatter block stripped.</summary>
    public required string Body { get; init; }
    /// <summary>Raw frontmatter key/values ({} when absent or malformed).</summary>
    public IReadOnlyDictionary<string, object?> Frontmatter { get; init; } =
        new Dictionary<string, object?>();
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> WikiLinks { get; init; } = [];
}
