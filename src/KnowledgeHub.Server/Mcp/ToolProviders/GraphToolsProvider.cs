using System.Text;
using System.Text.Json.Nodes;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Graph;
using KnowledgeHub.Server.Settings;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp.ToolProviders;

/// <summary>
/// Read-only knowledge-graph tools (SPEC-20260923-graphrag RF-004):
/// find_dependencies, find_dependents, find_path, analyze_impact.
/// All depth args clamp to 1..3 and results are hard-capped by the effective
/// MaxResults (default 200 edges). Absent entirely when the graph is disabled —
/// flag resolves through <see cref="IGraphSettingsService"/> so /settings
/// edits apply without restart (SPEC-20260923-graph-settings-ui RF-003).
/// </summary>
public sealed class GraphToolsProvider(IGraphSettingsService graphSettings) : IToolProvider
{
    private const int MaxDepth = 3;

    private static readonly JsonObject ComponentSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "component":{"type":"string","description":"Entity name (service, database, API, team…)","examples":["payments-api"]},
          "depth":{"type":"integer","description":"Traversal depth 1-3 (default 1, clamped to 3)"}
        },"required":["component"],
        "examples":[{"component":"payments-api","depth":2}]}
        """)!.AsObject();

    private static readonly JsonObject PathSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "a":{"type":"string","description":"Source entity name"},
          "b":{"type":"string","description":"Target entity name"},
          "depth":{"type":"integer","description":"Max path length 1-3 (default 3, clamped)"}
        },"required":["a","b"],
        "examples":[{"a":"web-frontend","b":"orders-db","depth":3}]}
        """)!.AsObject();

    private static readonly JsonObject ImpactSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "component":{"type":"string","description":"Entity name whose dependents are analyzed"}
        },"required":["component"],
        "examples":[{"component":"payments-api"}]}
        """)!.AsObject();

    public Task<IReadOnlyList<CatalogTool>> GetToolsAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!graphSettings.GetEffective().Enabled)
            return Task.FromResult<IReadOnlyList<CatalogTool>>([]);

        IReadOnlyList<CatalogTool> tools =
        [
            new CatalogTool
            {
                Name = "find_dependencies",
                Description = "What does this component depend on? Bounded outbound traversal of the knowledge graph — every edge carries evidence (chunk + document + source) from the indexed text.",
                InputSchema = ComponentSchema,
                ReadOnly = true,
                Handler = (ctx, ct) => TraverseAsync(ctx, ct, GraphDirection.Outbound)
            },
            new CatalogTool
            {
                Name = "find_dependents",
                Description = "What depends on this component? Bounded inbound traversal of the knowledge graph — every edge carries evidence (chunk + document + source).",
                InputSchema = ComponentSchema,
                ReadOnly = true,
                Handler = (ctx, ct) => TraverseAsync(ctx, ct, GraphDirection.Inbound)
            },
            new CatalogTool
            {
                Name = "find_path",
                Description = "Shortest path between two entities in the knowledge graph (depth-capped BFS).",
                InputSchema = PathSchema,
                ReadOnly = true,
                Handler = FindPathAsync
            },
            new CatalogTool
            {
                Name = "analyze_impact",
                Description = "Impact analysis: 1-hop dependents plus the affected documents behind each relation's evidence.",
                InputSchema = ImpactSchema,
                ReadOnly = true,
                Handler = AnalyzeImpactAsync
            }
        ];
        return Task.FromResult(tools);
    }

    private async ValueTask<CallToolResult> TraverseAsync(
        ToolCallContext ctx, CancellationToken ct, GraphDirection direction)
    {
        var component = ToolArgs.RequiredString(ctx, "component");
        var (store, maxEdges) = Resolve(ctx);
        var node = await store.FindNodeAsync(component, ct);
        if (node is null)
            return await UnknownComponent(store, component, ct);

        var depth = ClampDepth(ctx, out var clamped);
        var sub = await store.TraverseAsync(node.Id, direction, depth, maxEdges, ct);
        var payload = FormatSubgraph(sub.Nodes, sub.Edges, node, depth, clamped, sub.Truncated);
        var text = $"{component}: {sub.Edges.Count} edge(s), {sub.Nodes.Count} node(s)" +
            (sub.Truncated ? " (truncated at cap)" : "") +
            (clamped ? $" (depth clamped to {MaxDepth})" : "");
        return await ToolResults.Structured(text, payload);
    }

    private async ValueTask<CallToolResult> FindPathAsync(ToolCallContext ctx, CancellationToken ct)
    {
        var a = ToolArgs.RequiredString(ctx, "a");
        var b = ToolArgs.RequiredString(ctx, "b");
        var (store, _) = Resolve(ctx);
        var from = await store.FindNodeAsync(a, ct);
        if (from is null)
            return await UnknownComponent(store, a, ct);
        var to = await store.FindNodeAsync(b, ct);
        if (to is null)
            return await UnknownComponent(store, b, ct);

        var depth = ClampDepth(ctx, out var clamped);
        var paths = await store.FindPathsAsync(from.Id, to.Id, depth, 5, ct);
        if (paths.Count == 0)
        {
            return await ToolResults.Error(
                $"no path between '{a}' and '{b}' within depth {depth}");
        }

        var nodes = new Dictionary<Guid, KgNode>();
        foreach (var p in paths)
            foreach (var e in p)
            {
                nodes[e.FromNodeId] = e.From;
                nodes[e.ToNodeId] = e.To;
            }
        var payload = new
        {
            paths = paths.Select(p => p.Select(e => new
            {
                from = e.From.Name,
                to = e.To.Name,
                kind = e.Kind,
                evidence = EvidenceOf(e)
            })),
            depthClamped = clamped
        };
        return await ToolResults.Structured(
            $"path {a} → {b}: {paths[0].Count} hop(s)" + (clamped ? $" (depth clamped to {MaxDepth})" : ""),
            payload);
    }

    private async ValueTask<CallToolResult> AnalyzeImpactAsync(ToolCallContext ctx, CancellationToken ct)
    {
        var component = ToolArgs.RequiredString(ctx, "component");
        var (store, maxEdges) = Resolve(ctx);
        var node = await store.FindNodeAsync(component, ct);
        if (node is null)
            return await UnknownComponent(store, component, ct);

        var sub = await store.ImpactAsync(node.Id, maxEdges, ct);
        var docs = sub.Edges
            .Select(e => e.Document)
            .Where(d => d is not null)
            .DistinctBy(d => d.Id)
            .Select(d => new { title = d!.Title, uri = d.UriReference });
        var payload = new
        {
            component = node.Name,
            dependents = sub.Edges
                .GroupBy(e => e.Kind)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => new { from = e.From.Name, kind = e.Kind, evidence = EvidenceOf(e) })),
            affectedDocuments = docs,
            truncated = sub.Truncated
        };
        return await ToolResults.Structured(
            $"{component}: {sub.Edges.Count} dependent edge(s), {docs.Count()} affected document(s)",
            payload);
    }

    private (IKnowledgeGraphStore Store, int MaxEdges) Resolve(ToolCallContext ctx) =>
        (ctx.Services!.GetRequiredService<IKnowledgeGraphStore>(),
         graphSettings.GetEffective().MaxResults);

    private static int ClampDepth(ToolCallContext ctx, out bool clamped)
    {
        var depth = ToolArgs.OptionalInt(ctx, "depth", 1, 100);
        clamped = depth > MaxDepth || depth < 1;
        return Math.Clamp(depth, 1, MaxDepth);
    }

    private static object EvidenceOf(KgEdge e) => new
    {
        chunkId = e.EvidenceChunkId,
        docTitle = e.Document?.Title,
        uri = e.Document?.UriReference
    };

    private static object FormatSubgraph(
        IReadOnlyList<KgNode> nodes, IReadOnlyList<KgEdge> edges,
        KgNode root, int depth, bool clamped, bool truncated) => new
        {
            component = root.Name,
            depth,
            depthClamped = clamped,
            truncated,
            nodes = nodes.Select(n => new { id = n.Id, name = n.Name, type = n.Type }),
            edges = edges.Select(e => new
            {
                from = e.FromNodeId,
                to = e.ToNodeId,
                kind = e.Kind,
                evidence = EvidenceOf(e)
            })
        };

    private async ValueTask<CallToolResult> UnknownComponent(
        IKnowledgeGraphStore store, string component, CancellationToken ct)
    {
        var suggestions = await store.SuggestAsync(component, 5, ct);
        var sb = new StringBuilder($"unknown component '{component}'");
        if (suggestions.Count > 0)
            sb.Append(" — did you mean: ").Append(string.Join(", ", suggestions.Select(s => $"'{s.Name}'")));
        return await ToolResults.Error(sb.ToString());
    }
}
