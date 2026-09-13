using KnowledgeHub.Server.BackgroundServices;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Server.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

        return services;
    }
}
