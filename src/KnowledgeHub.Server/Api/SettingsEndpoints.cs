using KnowledgeHub.Server.Mcp.Upstream;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// Integration credential management (SPEC-20260916-firecrawl-mcp-proxy
/// RF-005/RF-006): masked listing plus save/remove per provider. Secrets are
/// written to the encrypted store and never echoed back — GET only exposes
/// hasKey + last-4 hint + source.
/// </summary>
public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/integrations", async (
            IIntegrationSecretStore store,
            IConfiguration cfg,
            CancellationToken ct) =>
        {
            var items = new List<IntegrationSettingsDto>();
            foreach (var provider in IntegrationProviders.All)
                items.Add(await DescribeAsync(provider, store, cfg, ct));
            return Results.Ok(new IntegrationSettingsResponse { Integrations = items });
        });

        group.MapPut("/integrations/{provider}", async (
            string provider,
            SetIntegrationKeyRequest? body,
            IIntegrationSecretStore store,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if (!IntegrationProviders.All.Contains(provider))
                return Results.NotFound(new { error = $"unknown provider '{provider}'" });

            var apiKey = body?.ApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
                return Results.BadRequest(new { error = "apiKey is required" });
            if (provider == IntegrationProviders.Firecrawl && !apiKey.StartsWith("fc-", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Firecrawl API keys start with 'fc-'" });
            if (provider == IntegrationProviders.Tavily && !apiKey.StartsWith("tvly-", StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Tavily API keys start with 'tvly-'" });

            await store.SetAsync(provider, apiKey, ct);
            await ResetProviderAsync(provider, services, ct);
            return Results.NoContent();
        });

        group.MapDelete("/integrations/{provider}", async (
            string provider,
            IIntegrationSecretStore store,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if (!IntegrationProviders.All.Contains(provider))
                return Results.NotFound(new { error = $"unknown provider '{provider}'" });

            await store.RemoveAsync(provider, ct);
            await ResetProviderAsync(provider, services, ct);
            return Results.NoContent();
        });

        return group;
    }

    private static async Task<IntegrationSettingsDto> DescribeAsync(
        string provider, IIntegrationSecretStore store, IConfiguration cfg, CancellationToken ct)
    {
        var info = await store.GetInfoAsync(provider, ct);
        var envKey = cfg[$"{ConfigSection(provider)}:ApiKey"];

        var (hasKey, hint, source) = info is not null
            ? (true, MaskHint(provider, info.KeyHint), "store")
            : !string.IsNullOrWhiteSpace(envKey)
                ? (true, MaskHint(provider, envKey.Length >= 4 ? envKey[^4..] : envKey), "env")
                : (false, null, "none");

        return new IntegrationSettingsDto
        {
            Provider = provider,
            DisplayName = provider switch
            {
                IntegrationProviders.Firecrawl => "Firecrawl",
                IntegrationProviders.DeepWiki => "DeepWiki",
                IntegrationProviders.Tavily => "Tavily",
                _ => provider
            },
            HasKey = hasKey,
            KeyHint = hint,
            Source = source,
            Note = provider switch
            {
                IntegrationProviders.Firecrawl =>
                    "Expõe as tools oficiais do Firecrawl (scrape, search, crawl…) no MCP interno.",
                IntegrationProviders.DeepWiki =>
                    "Com key, o DeepWiki usa o endpoint privado (mcp.devin.ai) — acesso a repos privados.",
                IntegrationProviders.Tavily =>
                    "Expõe as tools oficiais do Tavily (search, extract, map, crawl, research) no MCP interno.",
                _ => null
            }
        };
    }

    private static string ConfigSection(string provider) => provider switch
    {
        IntegrationProviders.Firecrawl => "Firecrawl",
        IntegrationProviders.DeepWiki => "DeepWiki",
        IntegrationProviders.Tavily => "Tavily",
        _ => provider
    };

    private static string MaskHint(string provider, string last4) =>
        provider == IntegrationProviders.Firecrawl ? $"fc-••••{last4}"
            : provider == IntegrationProviders.Tavily ? $"tvly-••••{last4}"
            : $"••••{last4}";

    /// <summary>Drops the provider's upstream session so the next call picks up
    /// the new effective credential (and, for DeepWiki, the new endpoint), then
    /// notifies the catalog — the effective tool list changed.</summary>
    private static async Task ResetProviderAsync(string provider, IServiceProvider services, CancellationToken ct)
    {
        switch (provider)
        {
            case IntegrationProviders.Firecrawl:
                await services.GetRequiredService<FirecrawlUpstreamClient>().ResetAsync();
                services.GetRequiredService<FirecrawlToolsProvider>().InvalidateToolsCache();
                break;
            case IntegrationProviders.DeepWiki:
                await services.GetRequiredService<DeepWikiUpstreamClient>().ResetAsync();
                break;
            case IntegrationProviders.Tavily:
                await services.GetRequiredService<TavilyUpstreamClient>().ResetAsync();
                services.GetRequiredService<TavilyToolsProvider>().InvalidateToolsCache();
                break;
        }
        await services.GetRequiredService<Mcp.IToolCatalogChangeNotifier>().NotifyToolsChangedAsync(ct);
    }
}
