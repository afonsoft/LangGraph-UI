using System.Text;
using System.Text.Json.Nodes;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Ingestion;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp.ToolProviders;

/// <summary>
/// Always-on tools (SPEC-04 RF-001/RF-002b/RF-002c):
/// search_knowledge, ask_knowledge, write_knowledge.
/// </summary>
public sealed class KnowledgeToolsProvider : IToolProvider
{
    private static readonly JsonObject SearchSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "query":{"type":"string","description":"Texto ou pergunta a buscar"},
          "topK":{"type":"integer","description":"Máx. de resultados (default 5, máx 50)"},
          "source":{"type":"string","description":"Slug da fonte (default: todas as ativas)"},
          "mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Modo de busca (default: hybrid)"}
        },"required":["query"]}
        """)!.AsObject();

    private static readonly JsonObject AskSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "question":{"type":"string","description":"Pergunta em linguagem natural"},
          "topK":{"type":"integer","description":"Máx. de passagens usadas como contexto (default 5, máx 50)"},
          "source":{"type":"string","description":"Slug da fonte (default: todas as ativas)"},
          "mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Modo de busca (default: hybrid)"}
        },"required":["question"]}
        """)!.AsObject();

    private static readonly JsonObject WriteSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "title":{"type":"string","description":"Título do documento (vira nome de arquivo em vaults)"},
          "content":{"type":"string","description":"Conteúdo em markdown/texto"},
          "source":{"type":"string","description":"Slug da fonte alvo (default: primeira ativa)"},
          "tags":{"type":"array","items":{"type":"string"},"description":"Tags (frontmatter em vaults)"}
        },"required":["title","content"]}
        """)!.AsObject();

    public Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogTool> tools =
        [
            new CatalogTool
            {
                Name = "search_knowledge",
                Description = "Busca semântica unificada em todas as fontes de conhecimento ativas. Retorna trechos rankeados com fonte, título e score.",
                InputSchema = SearchSchema,
                ReadOnly = true,
                Handler = async (ctx, ct) =>
                {
                    var query = ToolArgs.RequiredString(ctx, "query");
                    var topK = ToolArgs.OptionalInt(ctx, "topK", 5, 50);
                    var (sourceId, mode) = await ResolveScopeAsync(ctx, ct);
                    var search = ctx.Services!.GetRequiredService<ISearchService>();
                    var results = await search.SearchAsync(query, topK, sourceId, mode, ct);
                    return await ToolResults.Text(FormatHits(results));
                }
            },
            new CatalogTool
            {
                Name = "ask_knowledge",
                Description = "Responde uma pergunta usando o conhecimento indexado. Retorna contexto agregado com citações de fonte para o LLM sintetizar a resposta.",
                InputSchema = AskSchema,
                ReadOnly = true,
                Handler = async (ctx, ct) =>
                {
                    var question = ToolArgs.RequiredString(ctx, "question");
                    var topK = ToolArgs.OptionalInt(ctx, "topK", 5, 50);
                    var (sourceId, mode) = await ResolveScopeAsync(ctx, ct);
                    var search = ctx.Services!.GetRequiredService<ISearchService>();
                    var results = await search.SearchAsync(question, topK, sourceId, mode, ct);
                    return await ToolResults.Text(FormatAnswerContext(question, results));
                }
            },
            new CatalogTool
            {
                Name = "write_knowledge",
                Description = "Grava conteúdo na base de conhecimento. Em fontes ObsidianVault cria um arquivo .md; em outras fontes persiste um documento indexado imediatamente pesquisável.",
                InputSchema = WriteSchema,
                Handler = WriteKnowledgeAsync
            }
        ];
        return Task.FromResult(tools);
    }

    /// <summary>
    /// Resolves the optional `source` slug and `mode` args shared by
    /// search_knowledge/ask_knowledge (SPEC-20260914-hybrid-retrieval RF-003).
    /// </summary>
    private static async Task<(Guid? SourceId, SearchMode Mode)> ResolveScopeAsync(
        ToolCallContext ctx, CancellationToken ct)
    {
        var modeArg = ToolArgs.OptionalString(ctx, "mode");
        var mode = modeArg is null
            ? SearchMode.Hybrid
            : Enum.TryParse<SearchMode>(modeArg, ignoreCase: true, out var parsed)
                ? parsed
                : throw new McpProtocolException(
                    $"invalid mode '{modeArg}' (expected: hybrid | semantic | lexical)", McpErrorCode.InvalidParams);

        var sourceSlug = ToolArgs.OptionalString(ctx, "source");
        if (sourceSlug is null)
            return (null, mode);

        var db = ctx.Services!.GetRequiredService<KnowledgeHubDbContext>();
        var active = await db.Sources.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var slugs = ToolSlugger.Assign(active.Select(s => (s.Id, s.Name)));
        var sourceId = slugs.FirstOrDefault(kv => kv.Value == sourceSlug).Key;
        return sourceId == Guid.Empty
            ? throw new McpProtocolException($"unknown source slug '{sourceSlug}'", McpErrorCode.InvalidParams)
            : (sourceId, mode);
    }

    private static async ValueTask<CallToolResult> WriteKnowledgeAsync(
        ToolCallContext ctx, CancellationToken ct)
    {
        var title = ToolArgs.RequiredString(ctx, "title");
        var content = ToolArgs.RequiredString(ctx, "content");
        var sourceSlug = ToolArgs.OptionalString(ctx, "source");
        var tags = ToolArgs.OptionalStringArray(ctx, "tags");

        var db = ctx.Services!.GetRequiredService<KnowledgeHubDbContext>();
        var ingestion = ctx.Services!.GetRequiredService<IngestionService>();

        // Resolve target source: by slug, else first active source.
        var active = await db.Sources.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync(ct);
        if (active.Count == 0)
            return await ToolResults.Error("no active knowledge source configured");

        var slugs = ToolSlugger.Assign(active.Select(s => (s.Id, s.Name)));
        var target = sourceSlug is null
            ? active[0]
            : active.FirstOrDefault(s => slugs[s.Id] == sourceSlug)
              ?? throw new McpProtocolException($"unknown source slug '{sourceSlug}'", McpErrorCode.InvalidParams);

        if (target.SourceType == SourceType.ObsidianVault)
        {
            var root = IngestionService.ResolveVaultRoot(target.ConfigurationJson)
                ?? throw new McpProtocolException("vault source has no configured path", McpErrorCode.InvalidParams);
            var relative = ObsidianNoteWriter.TitleToFileName(title);
            var full = ObsidianNoteWriter.SafePath(root, relative, forWrite: true);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, ObsidianNoteWriter.WithFrontmatter(content, tags), ct);

            var relPath = Path.GetRelativePath(root, full);
            await ingestion.SyncFileAsync(target.Id, relPath, ct);

            var doc = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.KnowledgeSourceId == target.Id && d.UriReference == relPath, ct);
            var chunkCount = doc is null ? 0
                : await db.Chunks.CountAsync(c => c.KnowledgeDocumentId == doc.Id, ct);
            return await ToolResults.Text(
                $"Wrote `{relPath}` to vault '{target.Name}'.\nDocument id: {doc?.Id}\nChunks indexed: {chunkCount}");
        }

        // Non-vault source: persist + index an in-place KnowledgeDocument.
        var uriRef = $"knowledge://{slugs[target.Id]}/{ObsidianNoteWriter.TitleToFileName(title)}.md";
        var doc2 = await db.Documents.Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.KnowledgeSourceId == target.Id && d.UriReference == uriRef, ct);
        if (doc2 is null)
        {
            doc2 = new Domain.Entities.KnowledgeDocument
            {
                KnowledgeSourceId = target.Id,
                Title = title,
                UriReference = uriRef
            };
            db.Documents.Add(doc2);
        }
        else
        {
            doc2.Title = title;
            db.Chunks.RemoveRange(doc2.Chunks);
        }

        var body = ObsidianNoteWriter.WithFrontmatter(content, tags);
        doc2.RawContent = body;
        doc2.ContentHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        doc2.IndexedAt = DateTimeOffset.UtcNow;

        var embeddings = ctx.Services!.GetRequiredService<Embeddings.IEmbeddingProvider>();
        var vectors = ctx.Services!.GetRequiredService<VectorStore.IVectorStore>();
        var chunkTexts = MarkdownChunker.Chunk(body, 500, 50);
        var newChunks = chunkTexts.Select((t, i) => new Domain.Entities.DocumentChunk
        {
            KnowledgeDocumentId = doc2.Id,
            ChunkIndex = i,
            TextContent = t
        }).ToList();
        db.Chunks.AddRange(newChunks);
        await db.SaveChangesAsync(ct);

        foreach (var chunk in newChunks)
        {
            var vector = await embeddings.EmbedAsync(chunk.TextContent, ct);
            await vectors.UpsertAsync(chunk.Id, doc2.Id, target.Id, vector, embeddings.ModelId, ct);
        }

        // RF-004: keep the FTS index consistent with Chunks.
        await ctx.Services!.GetRequiredService<Search.ILexicalSearchService>().ReconcileAsync(ct);

        return await ToolResults.Text(
            $"Stored '{title}' in source '{target.Name}'.\nDocument id: {doc2.Id}\nChunks indexed: {newChunks.Count}");
    }

    internal static string FormatHits(IReadOnlyList<SearchResultItem> results)
    {
        if (results.Count == 0)
            return "No results found in active knowledge sources.";
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.Append("### ").Append(r.DocumentTitle).Append('\n')
              .Append("- source: ").Append(r.SourceName)
              .Append(" | score: ").Append(r.Score.ToString("F3"))
              .Append(" | uri: ").Append(r.UriReference).Append('\n')
              .Append(r.ChunkText).Append("\n\n");
        }
        return sb.ToString();
    }

    internal static string FormatAnswerContext(string question, IReadOnlyList<SearchResultItem> results)
    {
        var sb = new StringBuilder();
        sb.Append("Question: ").Append(question).Append("\n\n");
        if (results.Count == 0)
            return sb.Append("No relevant knowledge found. Answer from general knowledge and state that the knowledge base had no matches.").ToString();

        sb.Append("Retrieved context (cite sources in your answer):\n\n");
        var i = 1;
        foreach (var r in results)
        {
            sb.Append('[').Append(i++).Append("] ")
              .Append(r.DocumentTitle).Append(" — ").Append(r.SourceName)
              .Append(" (score ").Append(r.Score.ToString("F3")).Append(", ")
              .Append(r.UriReference).Append(")\n")
              .Append(r.ChunkText).Append("\n\n");
        }
        return sb.ToString();
    }
}
