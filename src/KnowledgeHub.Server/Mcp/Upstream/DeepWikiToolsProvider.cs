using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>
/// DeepWiki proxy tools (SPEC-07 RF-002/RF-003/RF-004) — names are
/// byte-identical to upstream (transparent bypass): ask_question,
/// read_wiki_structure, read_wiki_contents.
/// </summary>
public sealed partial class DeepWikiToolsProvider(
    DeepWikiUpstreamClient upstream,
    IOptions<DeepWikiOptions> options) : IToolProvider
{
    private static readonly JsonObject AskSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "repoName":{"anyOf":[{"type":"string"},{"type":"array","items":{"type":"string"},"maxItems":10}],
                      "description":"GitHub repo(s) in owner/repo format (max 10)"},
          "question":{"type":"string","description":"Question about the repository"}
        },"required":["repoName","question"]}
        """)!.AsObject();

    private static readonly JsonObject RepoSchema = JsonNode.Parse("""
        {"type":"object","properties":{
          "repoName":{"type":"string","description":"GitHub repo in owner/repo format"}
        },"required":["repoName"]}
        """)!.AsObject();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoNamePattern();

    public Task<IReadOnlyList<CatalogTool>> GetToolsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return Task.FromResult<IReadOnlyList<CatalogTool>>([]);

        IReadOnlyList<CatalogTool> tools =
        [
            new CatalogTool
            {
                Name = "ask_question",
                Description = "Ask any question about a GitHub repository (proxied to DeepWiki).",
                InputSchema = AskSchema,
                ReadOnly = true,
                Handler = async (ctx, ct) =>
                {
                    ValidateRepoArg(ctx, out _);
                    var question = ToolArgs.RequiredString(ctx, "question");
                    return await upstream.CallAsync("ask_question", ctx.Params?.Arguments, ct);
                }
            },
            new CatalogTool
            {
                Name = "read_wiki_structure",
                Description = "Get the documentation topic list for a GitHub repository (proxied to DeepWiki).",
                InputSchema = RepoSchema,
                ReadOnly = true,
                Handler = async (ctx, ct) =>
                {
                    ValidateRepoArg(ctx, out _);
                    return await upstream.CallAsync("read_wiki_structure", ctx.Params?.Arguments, ct);
                }
            },
            new CatalogTool
            {
                Name = "read_wiki_contents",
                Description = "Read documentation contents for a GitHub repository (proxied to DeepWiki).",
                InputSchema = RepoSchema,
                ReadOnly = true,
                Handler = async (ctx, ct) =>
                {
                    ValidateRepoArg(ctx, out _);
                    return await upstream.CallAsync("read_wiki_contents", ctx.Params?.Arguments, ct);
                }
            }
        ];
        return Task.FromResult(tools);
    }

    /// <summary>
    /// Client-side validation (SPEC-07 RF-004): repoName is a string or
    /// ≤10-element string array, each element matching owner/repo.
    /// </summary>
    public static void ValidateRepoArg(RequestContext<CallToolRequestParams> ctx, out IReadOnlyList<string> repos)
    {
        var arguments = ctx.Params?.Arguments;
        if (arguments is null || !arguments.TryGetValue("repoName", out var el))
            throw new McpProtocolException("missing required argument 'repoName'", McpErrorCode.InvalidParams);
        repos = ValidateRepoName(el);
    }

    /// <summary>Pure validation of the repoName JSON value — unit-testable.</summary>
    public static IReadOnlyList<string> ValidateRepoName(JsonElement el)
    {
        List<string> repos = el.ValueKind switch
        {
            JsonValueKind.String => [el.GetString()!],
            JsonValueKind.Array => el.EnumerateArray().Select(e => e.GetString() ?? "").ToList(),
            _ => throw new McpProtocolException("'repoName' must be a string or an array of strings", McpErrorCode.InvalidParams)
        };

        if (repos.Count == 0 || repos.Count > 10)
            throw new McpProtocolException("'repoName' accepts between 1 and 10 repositories", McpErrorCode.InvalidParams);
        foreach (var repo in repos)
            if (!RepoNamePattern().IsMatch(repo))
                throw new McpProtocolException($"invalid repoName '{repo}' — expected owner/repo", McpErrorCode.InvalidParams);

        return repos;
    }
}
