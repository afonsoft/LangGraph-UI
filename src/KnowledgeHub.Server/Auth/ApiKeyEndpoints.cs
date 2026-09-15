using System.Security.Claims;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Auth;

/// <summary>
/// /api/apikeys management (SPEC-20260914-auth-login RF-010) — cookie sessions
/// only: API keys cannot create or revoke API keys.
/// </summary>
public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeysApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/apikeys")
            .RequireAuthorization(AuthPolicies.CookieSession);

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapDelete("/{id:guid}", RevokeAsync);

        return group;
    }

    private static async Task<IResult> ListAsync(
        HttpContext http, KnowledgeHubDbContext db, CancellationToken ct)
    {
        var userId = CurrentUserId(http);
        var keys = await db.ApiKeys
            .Where(k => k.UserId == userId)
            .Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt, k.RevokedAt))
            .ToListAsync(ct);
        // SQLite cannot ORDER BY DateTimeOffset — sort client-side.
        return Results.Ok(keys.OrderByDescending(k => k.CreatedAt).ToList());
    }

    private static async Task<IResult> CreateAsync(
        CreateApiKeyRequest request,
        HttpContext http,
        KnowledgeHubDbContext db,
        CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
            return Results.BadRequest(new { error = "nome é obrigatório (máx. 100 caracteres)" });

        var secret = ApiKeyService.GenerateKey();
        var key = new ApiKey
        {
            Name = name,
            KeyHash = ApiKeyService.HashKey(secret),
            Prefix = ApiKeyService.PrefixOf(secret),
            UserId = CurrentUserId(http)
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return Results.Json(
            new ApiKeyCreatedDto(key.Id, key.Name, key.Prefix, secret),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id, HttpContext http, KnowledgeHubDbContext db, CancellationToken ct)
    {
        var key = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == id && k.UserId == CurrentUserId(http), ct);
        if (key is null)
            return Results.NotFound(new { error = "api key não encontrada" });

        key.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Guid CurrentUserId(HttpContext http) =>
        Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
}
