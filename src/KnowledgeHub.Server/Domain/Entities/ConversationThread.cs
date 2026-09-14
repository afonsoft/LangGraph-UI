namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>Persistent agent conversation (SPEC-20260914-conversation-threads RF-001).</summary>
public sealed class ConversationThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Rolling summary of messages that fell outside the context window.</summary>
    public string? Summary { get; set; }
    public string? MetadataJson { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}
