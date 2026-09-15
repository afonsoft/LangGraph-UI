using System.Security.Cryptography;
using System.Text;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// `aft_*` API key lifecycle (SPEC-20260914-auth-login RF-004): generation,
/// SHA-256 hashing, lookup. The full secret exists only at creation time —
/// persistence stores hash + display prefix.
/// </summary>
public static class ApiKeyService
{
    public const string KeyPrefix = "aft_";

    /// <summary>`aft_` + 32 lowercase hex chars (GUID format "N") — header-safe.</summary>
    public static string GenerateKey() => KeyPrefix + Guid.NewGuid().ToString("N");

    public static string HashKey(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string PrefixOf(string key) => key[..12];

    /// <summary>Finds the non-revoked key + owner for a presented secret.</summary>
    public static Task<ApiKey?> FindActiveAsync(KnowledgeHubDbContext db, string key, CancellationToken ct = default) =>
        db.ApiKeys.Include(k => k.User)
            .FirstOrDefaultAsync(k => k.KeyHash == HashKey(key) && k.RevokedAt == null, ct);
}
