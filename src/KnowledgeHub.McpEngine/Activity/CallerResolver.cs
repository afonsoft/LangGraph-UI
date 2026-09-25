using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Resolves the authenticated caller for audit events from the ambient
/// <see cref="IHttpContextAccessor"/> (AsyncLocal — correct inside request
/// filters). Claim names mirror the server's auth handlers; the engine must
/// not reference the server assembly, so the literals are duplicated here.
/// Never throws — anonymous/missing context yields null.
/// </summary>
internal static class CallerResolver
{
    // Mirrors ApiKeyAuthenticationHandler.AuthMethodClaim / KeyIdClaim.
    private const string AuthMethodClaim = "auth_method";
    private const string KeyIdClaim = "key_id";

    public static string? Resolve(IServiceProvider? services)
    {
        try
        {
            var http = services?.GetService<IHttpContextAccessor>()?.HttpContext;
            return Resolve(http?.User);
        }
        catch
        {
            return null;
        }
    }

    public static string? Resolve(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;
        var name = user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
        var method = user.FindFirst(AuthMethodClaim)?.Value ?? "cookie";
        var keyId = user.FindFirst(KeyIdClaim)?.Value;
        return method == "apikey" && Guid.TryParse(keyId, out var id)
            ? $"{name} (apikey:{id.ToString("N")[..8]})"
            : $"{name} ({method})";
    }
}
