using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using static KnowledgeHub.Tests.Integration.TestMcp;

namespace KnowledgeHub.Tests.Integration;

// Covers SPEC-20260914-mcp-contract-tests RF-001/RF-003/RF-004.
//
// Contract update rule: the MCP tool catalog IS a public interface — clients
// (Cursor, Claude Desktop) hardcode these names/schemas. A deliberate breaking
// change is fine: update the pinned names/schemas in this file in the same
// commit so the diff is reviewable. Failures print expected vs actual JSON.
public class McpContractTests : IClassFixture<McpContractTests.Fixture>
{
    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"kh-contract-{Guid.NewGuid():N}.db");
        public string Vault { get; } = Path.Combine(Path.GetTempPath(), $"vault-contract-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Vault);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = DbPath,
                    ["DeepWiki:Enabled"] = "true"
                }));
        }
    }

    private readonly Fixture _factory;

    public McpContractTests(Fixture factory) => _factory = factory;

    private static readonly Dictionary<string, string> PinnedSchemas = new()
    {
        ["search_knowledge"] = """{"type":"object","properties":{"query":{"type":"string","description":"Texto ou pergunta a buscar"},"topK":{"type":"integer","description":"Máx. de resultados (default 5, máx 50)"},"source":{"type":"string","description":"Slug da fonte (default: todas as ativas)"},"mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Modo de busca (default: hybrid)"}},"required":["query"]}""",
        ["ask_knowledge"] = """{"type":"object","properties":{"question":{"type":"string","description":"Pergunta em linguagem natural"},"topK":{"type":"integer","description":"Máx. de passagens usadas como contexto (default 5, máx 50)"},"source":{"type":"string","description":"Slug da fonte (default: todas as ativas)"},"mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Modo de busca (default: hybrid)"},"generate":{"type":"boolean","description":"Sintetizar resposta via LLM configurado no servidor (default: true quando Chat:Provider configurado)"}},"required":["question"]}""",
        ["agent_chat"] = """{"type":"object","properties":{"prompt":{"type":"string","description":"Pergunta/tarefa em linguagem natural — o agente itera tools até responder"},"tools":{"type":"array","items":{"type":"string"},"description":"Allowlist de tools expostas ao modelo (default: todas as read-only)"},"maxIterations":{"type":"integer","description":"Teto de iterações model→tools→model (default 10)"},"allowWrite":{"type":"boolean","description":"Opt-in: expõe tools de escrita (write_knowledge, write_note)"}},"required":["prompt"]}""",
        ["write_knowledge"] = """{"type":"object","properties":{"title":{"type":"string","description":"Título do documento (vira nome de arquivo em vaults)"},"content":{"type":"string","description":"Conteúdo em markdown/texto"},"source":{"type":"string","description":"Slug da fonte alvo (default: primeira ativa)"},"tags":{"type":"array","items":{"type":"string"},"description":"Tags (frontmatter em vaults)"}},"required":["title","content"]}""",
        ["read_document"] = """{"type":"object","properties":{"path":{"type":"string","description":"Caminho relativo da nota dentro do vault (ex.: 'pasta/nota.md')"},"source":{"type":"string","description":"Slug do vault (default: primeiro vault ativo)"}},"required":["path"]}""",
        ["write_note"] = """{"type":"object","properties":{"path":{"type":"string","description":"Caminho relativo da nota (.md é acrescentado se ausente)"},"content":{"type":"string","description":"Conteúdo markdown"},"tags":{"type":"array","items":{"type":"string"},"description":"Tags → frontmatter"},"source":{"type":"string","description":"Slug do vault (default: primeiro vault ativo)"}},"required":["path","content"]}""",
        ["ask_question"] = """{"type":"object","properties":{"repoName":{"anyOf":[{"type":"string"},{"type":"array","items":{"type":"string"},"maxItems":10}],"description":"GitHub repo(s) in owner/repo format (max 10)"},"question":{"type":"string","description":"Question about the repository"}},"required":["repoName","question"]}""",
        ["read_wiki_structure"] = """{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repo in owner/repo format"}},"required":["repoName"]}""",
        ["read_wiki_contents"] = """{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repo in owner/repo format"}},"required":["repoName"]}""",
        // query_{slug} tools share this schema (SourceQueryToolsProvider).
        ["__query_source__"] = """{"type":"object","properties":{"query":{"type":"string","description":"Texto ou pergunta a buscar nesta fonte"},"topK":{"type":"integer","description":"Máx. de resultados (default 5, máx 50)"}},"required":["query"]}""",
    };

    private static readonly HashSet<string> WriteTools = ["write_knowledge", "write_note"];

    [Fact]
    public async Task ToolCatalog_NamesSchemasAndHints_MatchPinnedContract()
    {
        var mcp = await ConnectAsync(_factory);
        var http = _factory.CreateClient();

        var sourceName = $"ContractVault{Guid.NewGuid():N}";
        var create = await http.PostAsJsonAsync("/api/sources", new
        {
            name = sourceName,
            type = "ObsidianVault",
            configuration = new { path = _factory.Vault },
            isActive = true
        });
        create.EnsureSuccessStatusCode();

        var result = await mcp.SendAsync("tools/list");
        var tools = result.GetProperty("tools").EnumerateArray().ToList();

        var slug = sourceName.ToLowerInvariant();
        var expectedNames = PinnedSchemas.Keys.Where(k => k != "__query_source__")
            .Append($"query_{slug}").Order().ToList();
        var actualNames = tools.Select(t => t.GetProperty("name").GetString()!).Order().ToList();
        Assert.Equal(string.Join(", ", expectedNames), string.Join(", ", actualNames));

        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString()!;
            var key = name.StartsWith("query_") ? "__query_source__" : name;
            using var expectedDoc = JsonDocument.Parse(PinnedSchemas[key]);
            var expected = Canonicalize(expectedDoc.RootElement);
            var actual = Canonicalize(tool.GetProperty("inputSchema"));
            Assert.True(expected == actual,
                $"inputSchema drift for '{name}'\nexpected: {expected}\nactual:   {actual}");

            var expectedReadOnly = !WriteTools.Contains(name);
            var actualReadOnly = tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean();
            Assert.True(expectedReadOnly == actualReadOnly,
                $"annotations.readOnlyHint drift for '{name}': expected {expectedReadOnly}, got {actualReadOnly}");
        }
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_IsMethodNotFound()
    {
        var mcp = await ConnectAsync(_factory);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mcp.SendAsync("tools/call", new { name = "removed_tool", arguments = new { } }));
        Assert.Contains("-32601", ex.Message);
    }

    // Canonical JSON: object keys sorted recursively so ordering doesn't
    // produce false-positive diffs.
    private static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            Write(writer, element);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    Write(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
