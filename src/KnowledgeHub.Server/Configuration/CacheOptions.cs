namespace KnowledgeHub.Server.Configuration;

/// <summary>
/// Distributed-cache configuration (SPEC-20260916-performance-memory-cache
/// RF-005). <c>memory</c> (default) keeps the zero-infra single-process
/// deploy; <c>redis</c> switches <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// to StackExchangeRedis for multi-replica/restart-surviving entries.
/// </summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary><c>memory</c> (default) | <c>redis</c>.</summary>
    public string Provider { get; set; } = "memory";

    /// <summary>Whether tool execution caching is enabled (SPEC-20260924-redis-cache-and-tool-caching RF-002).</summary>
    public bool ToolCacheEnabled { get; set; } = true;

    /// <summary>TTL in minutes for cached tool calls (minimum 60 minutes / 1 hour per user specification).</summary>
    public int ToolCacheTtlMinutes { get; set; } = 60;

    /// <summary>StackExchange.Redis connection string — required when
    /// Provider=redis. Use <c>defaultDatabase=N</c> to pin the logical DB.</summary>
    public RedisCacheOptions Redis { get; set; } = new();

    public sealed class RedisCacheOptions
    {
        public string ConnectionString { get; set; } = "";
    }
}
