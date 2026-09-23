using System.Text.Json;
using KnowledgeHub.McpEngine.Activity;
using Microsoft.AspNetCore.DataProtection;
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
        // SPEC-20260922-per-key-integration-secrets RF-000: caller identity for
        // per-key settings/upstream resolution — previously unregistered, which
        // silently disabled every per-key code path.
        services.AddHttpContextAccessor();

        // Resolve Database:Path lazily so test hosts can override it via ConfigureWebHost
        // (Program.cs runs before the factory's ConfigureAppConfiguration callbacks).
        // SPEC-20260916-performance-memory-cache RF-006: pooled contexts — one
        // DbContext allocation per request instead of a fresh graph each time.
        services.AddDbContextPool<KnowledgeHubDbContext>((sp, o) =>
            o.UseSqlite($"Data Source={DatabasePath.Resolve(sp.GetRequiredService<IConfiguration>())}"));

        services.AddOptions<EmbeddingOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(EmbeddingOptions.SectionName).Bind(options));

        services.AddHttpClient("embeddings");
        services.AddHttpClient("webpage", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("notion", c => c.Timeout = TimeSpan.FromSeconds(30));

        // SPEC-20260914-webpage-docfile-connectors: connector registry.
        services.AddSingleton<Ingestion.Connectors.ISourceConnector, Ingestion.Connectors.WebPageConnector>();
        services.AddSingleton<Ingestion.Connectors.ISourceConnector, Ingestion.Connectors.DocumentFileConnector>();
        services.AddSingleton<Ingestion.Connectors.ISourceConnector, Ingestion.Connectors.NotionConnector>();
        services.AddSingleton<IEmbeddingProvider>(sp =>
            EmbeddingProviderFactory.Create(
                sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value,
                sp.GetRequiredService<IHttpClientFactory>()));

        // SPEC-20260914-llm-answer-synthesis RF-001: optional chat client.
        // Provider=none → GetClient() returns null; consumers use GetService.
        services.AddOptions<Chat.ChatProviderOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(Chat.ChatProviderOptions.SectionName).Bind(options));
        services.AddHttpClient("chat");
        // SPEC-20260916-settings-chat-config RF-003: effective config comes from
        // the Settings store when a ChatSettings row exists, else env — the client
        // is resolved per scope (nullable) so /settings edits apply without restart.
        // Func<HttpClient> is an optional test seam for the connection probe.
        services.AddSingleton<Settings.IChatSettingsService>(sp => new Settings.ChatSettingsService(
            sp.GetRequiredService<IOptions<Chat.ChatProviderOptions>>(),
            sp.GetRequiredService<Settings.IIntegrationSecretStore>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<Settings.ChatSettingsService>>(),
            sp.GetService<Func<HttpClient>>()));
        services.AddSingleton<Settings.IApiKeyChatSettingsService>(sp => new Settings.ApiKeyChatSettingsService(
            sp.GetRequiredService<Settings.IChatSettingsService>(),
            sp.GetRequiredService<Settings.IIntegrationSecretStore>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<Settings.ApiKeyChatSettingsService>>()));
        services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
        {
            var http = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext;
            var keyIdValue = http?.User.FindFirst(Auth.ApiKeyAuthenticationHandler.KeyIdClaim)?.Value;
            if (keyIdValue is not null && Guid.TryParse(keyIdValue, out var keyId))
            {
                return sp.GetRequiredService<Settings.IApiKeyChatSettingsService>().GetClient(keyId)!;
            }
            return sp.GetRequiredService<Settings.IChatSettingsService>().GetClient()!;
        });
        services.AddScoped<IAnswerService>(sp =>
        {
            var http = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext;
            var keyIdValue = http?.User.FindFirst(Auth.ApiKeyAuthenticationHandler.KeyIdClaim)?.Value;
            if (keyIdValue is not null && Guid.TryParse(keyIdValue, out var keyId))
            {
                return new AnswerService(
                    sp.GetService<Microsoft.Extensions.AI.IChatClient>(),
                    sp.GetRequiredService<Settings.IApiKeyChatSettingsService>().GetEffectiveOptions(keyId),
                    sp.GetRequiredService<ILogger<AnswerService>>());
            }
            return new AnswerService(
                sp.GetService<Microsoft.Extensions.AI.IChatClient>(),
                sp.GetRequiredService<Settings.IChatSettingsService>().GetEffectiveOptions(),
                sp.GetRequiredService<ILogger<AnswerService>>());
        });

        // SPEC-20260914-agent-chat-loop: model→tools→model loop over the live catalog.
        services.AddOptions<Agent.AgentOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(Agent.AgentOptions.SectionName).Bind(options));
        services.AddScoped<IAgentService>(sp => new AgentService(
            sp.GetService<Microsoft.Extensions.AI.IChatClient>(),
            sp,
            sp.GetRequiredService<IDynamicToolCatalog>(),
            sp.GetRequiredService<Data.KnowledgeHubDbContext>(),
            sp.GetRequiredService<IOptions<Agent.AgentOptions>>().Value,
            sp.GetService<IMcpActivityFeed>(),
            sp.GetRequiredService<ILogger<AgentService>>()));
        services.AddScoped<IApprovalService>(sp => new ApprovalService(
            sp.GetRequiredService<Data.KnowledgeHubDbContext>(),
            TimeSpan.FromMinutes(
                sp.GetRequiredService<IOptions<Agent.AgentOptions>>().Value.ApprovalTimeoutMinutes),
            sp.GetService<IMcpActivityFeed>()));
        services.AddScoped<IConversationService, ConversationService>();

        services.AddScoped<IVectorStore>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var provider = cfg.GetValue("VectorStore:Provider", "sqlite");
            if (provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
                return new PostgresVectorStore(
                    cfg.GetValue<string>("VectorStore:ConnectionString")
                        ?? throw new InvalidOperationException("VectorStore:ConnectionString is required when VectorStore:Provider=postgres"),
                    cfg.GetValue("Embeddings:Dimensions", 384),
                    cfg.GetSection(PostgresOptions.SectionName).Get<PostgresOptions>() ?? new PostgresOptions());
            // SPEC-20260917-sqlite-vec-search: opt-in native KNN via the
            // sqlite-vec vec0 extension; "sqlite" stays the default.
            if (provider.Equals("sqlite-vec", StringComparison.OrdinalIgnoreCase))
                return new SqliteVecVectorStore(
                    sp.GetRequiredService<KnowledgeHubDbContext>(),
                    cfg.GetValue("Embeddings:Dimensions", 384));
            return new SqliteVectorStore(sp.GetRequiredService<KnowledgeHubDbContext>());
        });

        services.AddScoped<IKnowledgeSourceService, KnowledgeSourceService>();
        services.AddScoped<Search.ILexicalSearchService, Search.LexicalSearchService>();
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

        // SPEC-20260916-firecrawl-mcp-proxy RF-004: encrypted-at-rest upstream
        // credentials. DP key ring lives next to the DB so backup.sh can ship it.
        services.AddDataProtection()
            // Pinned app name: the default discriminator is the content root,
            // which differs between container (/app) and local runs — pinning
            // keeps stored secrets decryptable across deployments.
            .SetApplicationName("KnowledgeHub")
            .PersistKeysToFileSystem(new DirectoryInfo(
                Path.Combine(
                    Path.GetDirectoryName(DatabasePath.Resolve(configuration))!,
                    "dataprotection-keys")));
        services.AddSingleton<Settings.IIntegrationSecretStore, Settings.IntegrationSecretStore>();

        // SPEC-07: DeepWiki proxy tools (ask_question / read_wiki_structure / read_wiki_contents).
        services.AddOptions<KnowledgeHub.Server.Mcp.Upstream.DeepWikiOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(KnowledgeHub.Server.Mcp.Upstream.DeepWikiOptions.SectionName).Bind(options));
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.DeepWikiUpstreamClient>();
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.DeepWikiToolsProvider>();
        services.AddSingleton<IToolProvider>(sp =>
            sp.GetRequiredService<KnowledgeHub.Server.Mcp.Upstream.DeepWikiToolsProvider>());

        // SPEC-20260916-firecrawl-mcp-proxy: Firecrawl proxy tools (firecrawl_*).
        // Concrete-type registration lets SettingsEndpoints reset the client and
        // invalidate the provider's tools cache on key save/remove.
        services.AddOptions<KnowledgeHub.Server.Mcp.Upstream.FirecrawlOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(KnowledgeHub.Server.Mcp.Upstream.FirecrawlOptions.SectionName).Bind(options));
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.FirecrawlUpstreamClient>();
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.FirecrawlToolsProvider>();
        services.AddSingleton<IToolProvider>(sp =>
            sp.GetRequiredService<KnowledgeHub.Server.Mcp.Upstream.FirecrawlToolsProvider>());

        // SPEC-20260916-performance-memory-cache RF-005: IDistributedCache —
        // memory by default (zero-infra), Redis opt-in for shared/persistent
        // entries. Secrets never go through this store.
        services.AddOptions<Configuration.CacheOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(Configuration.CacheOptions.SectionName).Bind(options));
        services.AddMemoryCache();
        if (configuration.GetValue($"{Configuration.CacheOptions.SectionName}:Provider", "memory")
                .Equals("redis", StringComparison.OrdinalIgnoreCase))
        {
            var redisConnection = configuration
                .GetValue<string>($"{Configuration.CacheOptions.SectionName}:Redis:ConnectionString")
                ?? throw new InvalidOperationException(
                    "Cache:Redis:ConnectionString is required when Cache:Provider=redis");
            services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // SPEC-20260916-tavily-mcp-proxy: Tavily proxy tools (tavily_*).
        services.AddOptions<KnowledgeHub.Server.Mcp.Upstream.TavilyOptions>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(KnowledgeHub.Server.Mcp.Upstream.TavilyOptions.SectionName).Bind(options));
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.TavilyUpstreamClient>();
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.TavilyToolsProvider>();
        services.AddSingleton<IToolProvider>(sp =>
            sp.GetRequiredService<KnowledgeHub.Server.Mcp.Upstream.TavilyToolsProvider>());

        // SPEC-20260922-context7-mcp-proxy: Context7 proxy tools
        // (resolve-library-id / query-docs).
        services.AddOptions<KnowledgeHub.Server.Mcp.Upstream.Context7Options>()
            .Configure<IConfiguration>((options, cfg) =>
                cfg.GetSection(KnowledgeHub.Server.Mcp.Upstream.Context7Options.SectionName).Bind(options));
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.Context7UpstreamClient>();
        services.AddSingleton<KnowledgeHub.Server.Mcp.Upstream.Context7ToolsProvider>();
        services.AddSingleton<IToolProvider>(sp =>
            sp.GetRequiredService<KnowledgeHub.Server.Mcp.Upstream.Context7ToolsProvider>());

        // SPEC-20260917-mcp-proxy-source-type: generic upstream MCP proxies
        // driven by McpProxy sources (tools re-exposed with slug prefix).
        services.AddSingleton<IToolProvider, KnowledgeHub.Server.Mcp.Upstream.McpProxyToolsProvider>();
        services.AddSingleton<KnowledgeHub.Server.Mcp.ToolProviders.SettingsToolsProvider>();
        services.AddSingleton<IToolProvider>(sp =>
            sp.GetRequiredService<KnowledgeHub.Server.Mcp.ToolProviders.SettingsToolsProvider>());

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
                return await tool.Handler(
                    new KnowledgeHub.Server.Mcp.ToolCallContext
                    {
                        Services = ctx.Services!,
                        Arguments = ctx.Params?.Arguments
                    }, ct);
            };

            options.Handlers.ListResourcesHandler = async (ctx, ct) =>
                await KnowledgeResourceProvider.ListAsync(ctx.Services!, ct);

            options.Handlers.ReadResourceHandler = async (ctx, ct) =>
                await KnowledgeResourceProvider.ReadAsync(ctx.Params?.Uri ?? "", ctx.Services!, ct);
        });

        return services;
    }
}
