using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Server.VectorStore;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server;

/// <summary>Server-side DI composition (persistence, search, ingestion).</summary>
public static class KnowledgeHubServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeHubServer(this IServiceCollection services, IConfiguration configuration)
    {
        var dbPath = configuration.GetValue("Database:Path", "knowledgehub.db");
        services.AddDbContext<KnowledgeHubDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IEmbeddingProvider>(_ => new DeterministicEmbeddingProvider(
            configuration.GetValue("Embeddings:Dimensions", 384)));

        services.AddScoped<IVectorStore>(sp =>
        {
            var provider = configuration.GetValue("VectorStore:Provider", "sqlite");
            return provider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
                ? new PostgresVectorStore(
                    configuration.GetValue<string>("VectorStore:ConnectionString")
                        ?? throw new InvalidOperationException("VectorStore:ConnectionString is required when VectorStore:Provider=postgres"),
                    configuration.GetValue("Embeddings:Dimensions", 384))
                : new SqliteVectorStore(sp.GetRequiredService<KnowledgeHubDbContext>());
        });

        services.AddScoped<IKnowledgeSourceService, KnowledgeSourceService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddSingleton<IIngestionService, NotImplementedIngestionService>();

        return services;
    }
}
