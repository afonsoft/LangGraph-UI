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
    /// <summary>SPEC-20260923-source-authorization RF-001: JSON array of source
    /// GUIDs the key may read. Null = unrestricted; empty array = deny all.</summary>
    public string? AllowedSourceIdsJson { get; set; }
    /// <summary>JSON array of tool names the key may call. Null = unrestricted;
    /// empty array = deny all.</summary>
    public string? AllowedToolsJson { get; set; }
    /// <summary>SPEC-20260923-per-key-rate-limits RF-001: optional per-key
    /// overrides. NULL = inherit the global RateLimiting:* values.</summary>
    public int? LlmRateLimitPermits { get; set; }
    public int? LlmRateLimitWindowSeconds { get; set; }
    public int? SyncRateLimitPermits { get; set; }
    public int? SyncRateLimitWindowSeconds { get; set; }
    public List<ApiKeyUsageEvent> UsageEvents { get; set; } = [];
    public ApiKeyChatSettings? ChatSettings { get; set; }
}
