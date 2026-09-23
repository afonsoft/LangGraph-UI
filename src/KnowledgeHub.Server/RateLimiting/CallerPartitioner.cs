using System.Security.Claims;
using KnowledgeHub.Server.Auth;
using Microsoft.AspNetCore.Http;

namespace KnowledgeHub.Server.RateLimiting;

/// <summary>
/// SPEC-20260923-rate-limiting RF-001: resolves the rate-limit partition key
/// with precedence api-key → user → ip. The <c>anon:</c> prefix marks
/// unauthenticated callers so policies can apply stricter limits.
/// The key id / user id embedded here are opaque guids — the raw key secret
/// is never logged.
/// </summary>
public static class CallerPartitioner
{
    public enum Kind { ApiKey, User, Ip }

    public const string AnonymousPrefix = "anon:";

    public static (string Key, Kind Kind, bool IsAnonymous) Resolve(
        HttpContext? http, bool trustForwardedHeaders = false)
    {
        if (http is not null)
        {
            if (Guid.TryParse(
                    http.User.FindFirst(ApiKeyAuthenticationHandler.KeyIdClaim)?.Value, out var keyId))
                return ($"key:{keyId}", Kind.ApiKey, false);

            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
                return ($"user:{userId}", Kind.User, false);

            return ($"{AnonymousPrefix}{ClientIp(http, trustForwardedHeaders)}", Kind.Ip, true);
        }

        // stdio / in-process dispatch with no ambient HTTP context — shared bucket.
        return ("mcp:global", Kind.Ip, false);
    }

    private static string ClientIp(HttpContext http, bool trustForwardedHeaders)
    {
        if (trustForwardedHeaders
            && http.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded)
            && forwarded.Count > 0
            && forwarded[0]?.Split(',')[0].Trim() is { Length: > 0 } first)
            return first;

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
