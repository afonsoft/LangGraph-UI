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

    /// <summary>SPEC-20260923-retrieval-quality RF-005: v2 key embeds the
    /// filter fingerprint so filtered and unfiltered results never collide;
    /// the v2 prefix prevents stale v1 payloads from deserializing as hits.</summary>
    public static string Search(string mode, int topK, Guid? sourceId, string filterFingerprint, string query, string indexVersion) =>
        $"search:v2:{mode}:{topK}:{sourceId?.ToString("N") ?? "all"}:{Sha256(filterFingerprint)}:{Sha256(query)}:v{indexVersion}";

    /// <summary>Short stable content hash for cache keys.</summary>
    public static string Hash(string value) => Sha256(value);

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
}
