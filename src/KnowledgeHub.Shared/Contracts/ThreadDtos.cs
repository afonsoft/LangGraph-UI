namespace KnowledgeHub.Shared.Contracts;

/// <summary>Thread list item (SPEC-20260914-conversation-threads).</summary>
public sealed record ThreadDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset LastActivityAt { get; init; }
    public required int MessageCount { get; init; }
    /// <summary>True when older history was condensed — "contexto resumido" badge.</summary>
    public required bool Summarized { get; init; }
}

public sealed record ThreadMessageDto
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public string? ToolName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record ThreadDetailDto
{
    public required ThreadDto Thread { get; init; }
    public string? Summary { get; init; }
    public required IReadOnlyList<ThreadMessageDto> Messages { get; init; }
}

public sealed record CreateThreadRequest
{
    public string? Title { get; init; }
}

public sealed record RenameThreadRequest
{
    public required string Title { get; init; }
}

/// <summary>POST /api/threads/{id}/messages — runs the agent and persists both sides.</summary>
public sealed record PostThreadMessageRequest
{
    public required string Content { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
    public int? MaxIterations { get; init; }
    public bool AllowWrite { get; init; }
}
