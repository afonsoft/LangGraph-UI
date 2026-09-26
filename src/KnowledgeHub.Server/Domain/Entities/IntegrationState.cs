namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Runtime enable/disable flag per upstream integration (deepwiki, firecrawl,
/// tavily, context7). Independent of the secret row — DeepWiki works keyless,
/// so the flag cannot live on <see cref="IntegrationSecret"/>. Absent row = enabled.
/// </summary>
public sealed class IntegrationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Provider slug — same values as <see cref="IntegrationSecret.Provider"/>.</summary>
    public required string Provider { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
