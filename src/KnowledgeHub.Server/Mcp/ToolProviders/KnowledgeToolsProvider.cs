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
          "query":{"type":"string","description":"Text or question to search for","examples":["what is RAG?"]},
          "topK":{"type":"integer","description":"Max results (default 5, max 50)"},
          "source":{"type":"string","description":"Source slug (default: all active sources)"},
          "mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Search mode (default: hybrid)"}
        },"required":["query"],
        "examples":[{"query":"what is RAG?","topK":5,"mode":"hybrid"}]}
        """)!.AsObject();

    private static readonly JsonObject AskSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "question":{"type":"string","description":"Natural-language question","examples":["How does synchronization work?"]},
          "topK":{"type":"integer","description":"Max passages used as context (default 5, max 50)"},
          "source":{"type":"string","description":"Source slug (default: all active sources)"},
          "mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Search mode (default: hybrid)"},
          "generate":{"type":"boolean","description":"Synthesize the answer via the server's configured chat provider (default: true when one is configured)"}
        },"required":["question"],
        "examples":[{"question":"How does synchronization work?","topK":5,"generate":true}]}
        """)!.AsObject();

    private static readonly JsonObject AgentSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "prompt":{"type":"string","description":"Natural-language question or task — the agent iterates tools until it can answer","examples":["Summarize this week's notes"]},
          "tools":{"type":"array","items":{"type":"string"},"description":"Allowlist of tools exposed to the model (default: all read-only tools)","examples":[["search_knowledge","ask_knowledge"]]},
          "maxIterations":{"type":"integer","description":"Max model→tools→model iterations (default 10)"},
          "allowWrite":{"type":"boolean","description":"Opt-in: exposes write tools (write_knowledge, write_note)"},
          "threadId":{"type":"string","description":"Existing thread GUID — continues the conversation with context"},
          "persist":{"type":"boolean","description":"Creates a new thread and persists this call's turns"}
        },"required":["prompt"],
        "examples":[{"prompt":"Summarize this week's notes","tools":["search_knowledge","ask_knowledge"],"maxIterations":10,"persist":true}]}
        """)!.AsObject();

    private static readonly JsonObject WriteSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "title":{"type":"string","description":"Document title (becomes the file name in vault sources)","examples":["Example note"]},
          "content":{"type":"string","description":"Markdown or plain-text content","examples":["# Title\n\nMarkdown content."]},
          "source":{"type":"string","description":"Target source slug (default: first active source)"},
          "tags":{"type":"array","items":{"type":"string"},"description":"Tags (stored as frontmatter in vault sources)","examples":[["example"]]}
        },"required":["title","content"],
        "examples":[{"title":"Example note","content":"# Title\n\nMarkdown content.","tags":["example"]}]}
        """)!.AsObject();

    public Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogTool> tools =
        [
            new CatalogTool
            {
                Name = "search_knowledge",
                Description = "Unified semantic search across all active knowledge sources. Returns ranked passages with source name, document title, score and URI. Use for exploratory lookups; use a scoped query_* tool to search a single source.",
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
                Description = "Answers a natural-language question using the indexed knowledge base. When a chat provider is configured, returns a synthesized answer with [n] citations — each citation includes the document title, source name and file path, which can be passed to read_document to fetch the full document. Without a provider (or generate=false) returns the raw aggregated context.",
                InputSchema = AskSchema,
                ReadOnly = true,
                Handler = AskKnowledgeAsync
            },
            new CatalogTool
            {
                Name = "agent_chat",
                Description = "Multi-step agent: iterates model → tools → model over the live tool catalog until it can answer the prompt. Read-only tools are available by default; set allowWrite to expose write tools. Requires a configured chat provider.",
                InputSchema = AgentSchema,
                ReadOnly = true, // mutating tools still require allowWrite opt-in
                Handler = AgentChatAsync
            },
            new CatalogTool
            {
                Name = "write_knowledge",
                Description = "Persists content into the knowledge base. For markdown-vault sources it creates a .md file; for other sources it stores a document that is indexed and immediately searchable.",
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

    /// <summary>
    /// SPEC-20260914-llm-answer-synthesis RF-002/RF-004: with a configured chat
    /// provider and generate!=false, synthesizes a cited answer (structuredContent).
    /// Otherwise falls back to the legacy aggregated-context payload.
    /// </summary>
    private static async ValueTask<CallToolResult> AskKnowledgeAsync(
        ToolCallContext ctx, CancellationToken ct)
    {
        var question = ToolArgs.RequiredString(ctx, "question");
        var topK = ToolArgs.OptionalInt(ctx, "topK", 5, 50);
        var (sourceId, mode) = await ResolveScopeAsync(ctx, ct);
        var search = ctx.Services!.GetRequiredService<ISearchService>();
        var results = await search.SearchAsync(question, topK, sourceId, mode, ct);

        var answers = ctx.Services!.GetRequiredService<IAnswerService>();
        var generate = ToolArgs.OptionalBool(ctx, "generate") ?? answers.IsConfigured;

        if (!generate)
            return await ToolResults.Text(FormatAnswerContext(question, results));

        if (!answers.IsConfigured)
        {
            // RF risk mitigation: generate requested but no provider — raw context + warning.
            return await ToolResults.Text(
                FormatAnswerContext(question, results)
                + "\n\n(warning: no chat provider configured — returning raw context)");
        }

        try
        {
            var answer = await answers.AnswerAsync(question, results, ct);
            var text = new StringBuilder(answer.Answer);
            if (answer.Citations.Count > 0)
            {
                text.Append("\n\nCitations:");
                foreach (var c in answer.Citations)
                    text.Append("\n[").Append(c.Index).Append("] ")
                        .Append(c.Title).Append(" — ").Append(c.Source)
                        .Append(c.Path is not null ? " (path: " : " (")
                        .Append(c.Path ?? c.Uri).Append(')');
            }
            return await ToolResults.Structured(text.ToString(), answer);
        }
        catch (Chat.ChatProviderException ex)
        {
            return await ToolResults.Error($"answer generation failed: {ex.Message}");
        }
    }

    /// <summary>SPEC-20260914-agent-chat-loop RF-002: agent loop as an MCP tool.</summary>
    private static async ValueTask<CallToolResult> AgentChatAsync(
        ToolCallContext ctx, CancellationToken ct)
    {
        var agent = ctx.Services!.GetRequiredService<IAgentService>();
        if (!agent.IsConfigured)
            return await ToolResults.Error("agent_chat requires a chat provider (Chat:Provider)");

        var request = new AgentRequest
        {
            Prompt = ToolArgs.RequiredString(ctx, "prompt"),
            Tools = ToolArgs.OptionalStringArray(ctx, "tools"),
            MaxIterations = ToolArgs.OptionalInt(ctx, "maxIterations", 10, 50),
            AllowWrite = ToolArgs.OptionalBool(ctx, "allowWrite") == true,
            ThreadId = ToolArgs.OptionalString(ctx, "threadId") is { } tid && Guid.TryParse(tid, out var g) ? g : null,
            Persist = ToolArgs.OptionalBool(ctx, "persist") == true
        };

        try
        {
            var result = await agent.RunAsync(request, ct);
            var text = new StringBuilder(result.Answer);
            if (result.Steps.Count > 0)
            {
                text.Append("\n\nSteps:");
                foreach (var s in result.Steps)
                    text.Append("\n- [").Append(s.Iteration).Append("] ")
                        .Append(s.Tool).Append(' ').Append(s.ArgsSummary)
                        .Append(s.IsError ? " (error)" : "");
            }
            return await ToolResults.Structured(text.ToString(), result);
        }
        catch (Chat.ChatProviderException ex)
        {
            return await ToolResults.Error($"agent run failed: {ex.Message}");
        }
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

        if (ObsidianNoteWriter.IsReadOnly(target))
            return await ToolResults.Error($"source '{target.Name}' is read-only");

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
        // SPEC-20260923-code-aware-chunking: kind from the document URI.
        var (kind, pieces) = Ingestion.Chunking.ChunkerSelector.Chunk(doc2.UriReference, body, 500, 50);
        var newChunks = pieces.Select((p, i) => new Domain.Entities.DocumentChunk
        {
            KnowledgeDocumentId = doc2.Id,
            ChunkIndex = i,
            TextContent = p.Text,
            ChunkKind = kind.ToString().ToLowerInvariant(),
            SymbolPath = p.SymbolPath
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
