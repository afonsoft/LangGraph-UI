namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Upstream integration credential persisted per provider
/// (SPEC-20260916-firecrawl-mcp-proxy RF-004). Unlike <see cref="ApiKey"/> —
/// which the platform *issues* hashed — this stores a secret the platform
/// *consumes*, so it must be reversible: the value is protected with ASP.NET
/// Core Data Protection and only a last-4 hint is kept for display.
/// </summary>
public sealed class IntegrationSecret
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Provider slug — e.g. "firecrawl", "deepwiki" (unique).</summary>
    public required string Provider { get; set; }
    /// <summary>Data Protection ciphertext (base64) — never the raw secret.</summary>
    public required string ProtectedValue { get; set; }
    /// <summary>Last 4 characters of the secret, for masked display.</summary>
    public required string KeyHint { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
