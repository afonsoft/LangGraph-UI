using System.Text.Json.Nodes;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp.ToolProviders;

/// <summary>
/// One <c>query_{source_slug}</c> tool per active source (SPEC-04 RF-001).
/// Rebuilt per request — deactivation removes the tool immediately.
/// </summary>
public sealed class SourceQueryToolsProvider : IToolProvider
{
    private static readonly JsonObject Schema = JsonNode.Parse("""
        {"type":"object","properties":{
          "query":{"type":"string","description":"Texto ou pergunta a buscar nesta fonte"},
          "topK":{"type":"integer","description":"Máx. de resultados (default 5, máx 50)"}
        },"required":["query"]}
        """)!.AsObject();

    public async Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<KnowledgeHubDbContext>();
        var active = await db.Sources.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Description, s.SourceType })
            .ToListAsync(cancellationToken);

        var slugs = ToolSlugger.Assign(active.Select(s => (s.Id, s.Name)));
        return active.Select(source => new CatalogTool
        {
            Name = $"query_{slugs[source.Id]}",
            Description = $"Busca semântica exclusiva na fonte '{source.Name}' ({source.SourceType})" +
                          (string.IsNullOrWhiteSpace(source.Description) ? "." : $" — {source.Description}"),
            InputSchema = Schema,
            ReadOnly = true,
            Handler = async (ctx, ct) =>
            {
                var query = ToolArgs.RequiredString(ctx, "query");
                var topK = ToolArgs.OptionalInt(ctx, "topK", 5, 50);
                var search = ctx.Services!.GetRequiredService<ISearchService>();
                var results = await search.SearchAsync(query, topK, source.Id, ct: ct);
                return await ToolResults.Text(KnowledgeToolsProvider.FormatHits(results));
            }
        }).ToList();
    }
}
