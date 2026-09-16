using System.Security.Cryptography;
using System.Text;

namespace KnowledgeHub.Server.Caching;

/// <summary>Key conventions + index-version token for the distributed cache
/// (SPEC-20260916-performance-memory-cache §5).</summary>
public static class CacheKeys
{
    /// <summary>Opaque token bumped by ingestion on every sync; search-result
    /// keys embed it so index changes invalidate stale results immediately.</summary>
    public const string IndexVersion = "index:version";

    public static string Embedding(string modelId, string text) =>
        $"emb:{modelId}:{Sha256(text)}";

    public static string Search(string mode, int topK, Guid? sourceId, string query, string indexVersion) =>
        $"search:{mode}:{topK}:{sourceId?.ToString("N") ?? "all"}:{Sha256(query)}:v{indexVersion}";

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}
