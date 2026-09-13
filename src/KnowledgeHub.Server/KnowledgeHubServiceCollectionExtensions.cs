using System.Text.Json;
using KnowledgeHub.Server.BackgroundServices;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Server.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgeHub.Server;

/// <summary>Server-side DI composition (persistence, embeddings, search, ingestion).</summary>
public static class KnowledgeHubServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeHubServer(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolve Database:Path lazily so test hosts can override it via ConfigureWebHost
        // (Program.cs runs before the factory's ConfigureAppConfiguration callbacks).
        services.AddDbContext<KnowledgeHubDbContext>((sp, o) =>
            o.UseSqlite($"Data Source={sp.GetRequiredService<IConfiguration>()
                .GetValue("Database:Path", "knowledgehub.db")}"));

        services.AddOptions<EmbeddingOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(EmbeddingOptions.SectionName).Bind(options));

        services.AddHttpClient("embeddings");
        services.AddSingleton<IEmbeddingProvider>(sp =>
            EmbeddingProviderFactory.Create(
                sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value,
                sp.GetRequiredService<IHttpClientFactory>()));

        services.AddScoped<IVectorStore>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var provider = cfg.GetValue("VectorStore:Provider", "sqlite");
            return provider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
                ? new PostgresVectorStore(
                    cfg.GetValue<string>("VectorStore:ConnectionString")
                        ?? throw new InvalidOperationException("VectorStore:ConnectionString is required when VectorStore:Provider=postgres"),
                    cfg.GetValue("Embeddings:Dimensions", 384))
                : new SqliteVectorStore(sp.GetRequiredService<KnowledgeHubDbContext>());
        });

        services.AddScoped<IKnowledgeSourceService, KnowledgeSourceService>();
        services.AddScoped<ISearchService, SearchService>();

        services.AddSingleton<IngestionService>();
        services.AddSingleton<IIngestionService>(sp => sp.GetRequiredService<IngestionService>());
        services.AddHostedService<VaultWatcherService>();

        // SPEC-04: dynamic MCP tool catalog + handlers + change notifier.
        services.AddSingleton<IToolProvider, KnowledgeHub.Server.Mcp.ToolProviders.KnowledgeToolsProvider>();
        services.AddSingleton<IToolProvider, KnowledgeHub.Server.Mcp.ToolProviders.SourceQueryToolsProvider>();
        services.AddSingleton<IToolProvider, KnowledgeHub.Server.Mcp.ToolProviders.ObsidianToolsProvider>();
        services.AddSingleton<IDynamicToolCatalog, DynamicToolCatalog>();
        services.AddSingleton<IToolCatalogChangeNotifier, ToolCatalogChangeNotifier>();

        // SPEC-07: DeepWiki proxy tools (ask_question / read_wiki_structure / read_wiki_contents).
        services.AddOptions<KnowledgeHub.Server.Mcp.Upstream.DeepWikiOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(KnowledgeHub.Server.Mcp.Upstream.DeepWikiOptions.SectionName).Bind(options));
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.DeepWikiUpstreamClient>();
        services.AddSingleton<IToolProvider, KnowledgeHub.Server.Mcp.Upstream.DeepWikiToolsProvider>();

        services.AddOptions<McpServerOptions>().Configure(options =>
        {
            options.Handlers.ListToolsHandler = async (ctx, ct) =>
            {
                var catalog = ctx.Services!.GetRequiredService<IDynamicToolCatalog>();
                var tools = await catalog.GetToolsAsync(ctx.Services!, ct);
                return new ListToolsResult
                {
                    Tools = tools.Select(t => new Tool
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = JsonSerializer.SerializeToElement(t.InputSchema),
                        Annotations = new ToolAnnotations { ReadOnlyHint = t.ReadOnly }
                    }).ToList()
                };
            };

            options.Handlers.CallToolHandler = async (ctx, ct) =>
            {
                var catalog = ctx.Services!.GetRequiredService<IDynamicToolCatalog>();
                var name = ctx.Params?.Name;
                var tool = (await catalog.GetToolsAsync(ctx.Services!, ct))
                    .FirstOrDefault(t => t.Name == name)
                    ?? throw new McpProtocolException($"unknown tool '{name}'", McpErrorCode.MethodNotFound);
                return await tool.Handler(ctx, ct);
            };

            options.Handlers.ListResourcesHandler = async (ctx, ct) =>
                await KnowledgeResourceProvider.ListAsync(ctx.Services!, ct);

            options.Handlers.ReadResourceHandler = async (ctx, ct) =>
                await KnowledgeResourceProvider.ReadAsync(ctx.Params?.Uri ?? "", ctx.Services!, ct);
        });

        return services;
    }
}
