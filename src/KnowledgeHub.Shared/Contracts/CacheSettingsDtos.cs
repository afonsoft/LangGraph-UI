namespace KnowledgeHub.Shared.Contracts;

/// <summary>
/// Cache management and inspection DTOs (SPEC-20260924-redis-cache-and-tool-caching).
/// </summary>
public sealed class CacheStatsDto
{
    public string Provider { get; set; } = "memory";
    public bool IsConnected { get; set; } = true;
    public int TotalKeys { get; set; }
    public long TotalSizeBytes { get; set; }
    public long Hits { get; set; }
    public long Misses { get; set; }
    public IReadOnlyList<CacheKeyItemDto> Keys { get; set; } = [];
}

public sealed class CacheKeyItemDto
{
    public string Key { get; set; } = "";
    public string Prefix { get; set; } = "";
    public long SizeBytes { get; set; }
    public long? ExpiresInSeconds { get; set; }
}

public sealed class ClearCacheResultDto
{
    public bool Success { get; set; }
    public int ClearedKeys { get; set; }
    public string Message { get; set; } = "";
}
