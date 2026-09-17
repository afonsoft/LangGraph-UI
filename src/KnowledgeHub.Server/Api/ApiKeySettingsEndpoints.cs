using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// Per-API-key chat provider settings endpoints (SPEC-20260916-api-key-settings RF-003 expanded).
/// </summary>
public static class ApiKeySettingsEndpoints
{
    public static RouteGroupBuilder MapApiKeySettingsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/api-keys/{id:guid}/settings/chat");

        group.MapGet("/", async (
            Guid id,
            IApiKeyChatSettingsService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.DescribeAsync(id, ct));
        });

        group.MapPut("/", async (
            Guid id,
            SaveApiKeyChatSettingsRequest? body,
            IApiKeyChatSettingsService service,
            CancellationToken ct) =>
        {
            var endpoint = body?.Endpoint?.Trim();
            if (endpoint is not null && !string.IsNullOrEmpty(endpoint) && !IsHttpUri(endpoint))
                return Results.BadRequest(new { error = "endpoint must be an absolute http(s) URI" });

            await service.SaveAsync(id, body?.Endpoint, body?.Model, body?.ApiKey, ct);
            return Results.Ok(await service.DescribeAsync(id, ct));
        });

        group.MapDelete("/", async (
            Guid id,
            IApiKeyChatSettingsService service,
            CancellationToken ct) =>
        {
            await service.RemoveAsync(id, ct);
            return Results.NoContent();
        });

        // Integration keys (firecrawl, tavily)
        var integrations = app.MapGroup("/api/api-keys/{id:guid}/settings/integrations/{provider}");

        integrations.MapPut("/", async (
            Guid id,
            string provider,
            SetIntegrationKeyRequest? body,
            IApiKeyChatSettingsService service,
            CancellationToken ct) =>
        {
            if (!IntegrationProviders.All.Contains(provider))
                return Results.NotFound(new { error = $"unknown provider '{provider}'" });

            var apiKey = body?.ApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { error = "apiKey is required" });

            await service.SaveIntegrationKeyAsync(id, provider, apiKey, ct);
            return Results.Ok(new { provider, status = "saved" });
        });

        integrations.MapDelete("/", async (
            Guid id,
            string provider,
            IApiKeyChatSettingsService service,
            CancellationToken ct) =>
        {
            if (!IntegrationProviders.All.Contains(provider))
                return Results.NotFound(new { error = $"unknown provider '{provider}'" });

            await service.RemoveIntegrationKeyAsync(id, provider, ct);
            return Results.NoContent();
        });

        return group;
    }

    private static bool IsHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
