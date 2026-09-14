namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>One turn inside a <see cref="ConversationThread"/> (user | assistant | tool).</summary>
public sealed class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public ConversationThread? Thread { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public string? ToolName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>chars/4 heuristic — same convention as the chunker.</summary>
    public int TokenEstimate { get; set; }
}
