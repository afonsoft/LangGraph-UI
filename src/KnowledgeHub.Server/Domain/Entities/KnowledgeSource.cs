using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Domain.Entities;

public sealed class KnowledgeSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public SourceType SourceType { get; set; }
    public string? ConfigurationJson { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AutoSyncEnabled { get; set; }
    public int? SyncIntervalMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncAt { get; set; }

    public List<KnowledgeDocument> Documents { get; set; } = [];
}
