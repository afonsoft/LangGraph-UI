namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Per-client API key for non-browser access to /mcp, /api/* and /hubs/mcp
/// (SPEC-20260914-auth-login RF-004). Keys are `aft_<32hex>`; only the SHA-256
/// hash and a display prefix are stored — the secret is shown once at creation.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    /// <summary>SHA-256 hex of the full key — lookup index, never the secret.</summary>
    public required string KeyHash { get; set; }
    /// <summary>First 12 chars (`aft_xxxxxxxx`) for display/identification.</summary>
    public required string Prefix { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public List<ApiKeyUsageEvent> UsageEvents { get; set; } = [];
    public ApiKeyChatSettings? ChatSettings { get; set; }
}
