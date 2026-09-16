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

    /// <summary>StackExchange.Redis connection string — required when
    /// Provider=redis. Use <c>defaultDatabase=N</c> to pin the logical DB.</summary>
    public RedisCacheOptions Redis { get; set; } = new();

    public sealed class RedisCacheOptions
    {
        public string ConnectionString { get; set; } = "";
    }
}
