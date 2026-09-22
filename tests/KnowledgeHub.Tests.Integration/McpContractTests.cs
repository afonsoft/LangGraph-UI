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
        ["search_knowledge"] = """{"type":"object","properties":{"query":{"type":"string","description":"Texto ou pergunta a buscar","examples":["o que é RAG?"]},"topK":{"type":"integer","description":"Máx. de resultados (default 5, máx 50)"},"source":{"type":"string","description":"Slug da fonte (default: todas as ativas)"},"mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Modo de busca (default: hybrid)"}},"required":["query"],"examples":[{"query":"o que é RAG?","topK":5,"mode":"hybrid"}]}""",
        ["ask_knowledge"] = """{"type":"object","properties":{"question":{"type":"string","description":"Pergunta em linguagem natural","examples":["Como funciona a sincronização?"]},"topK":{"type":"integer","description":"Máx. de passagens usadas como contexto (default 5, máx 50)"},"source":{"type":"string","description":"Slug da fonte (default: todas as ativas)"},"mode":{"type":"string","enum":["hybrid","semantic","lexical"],"description":"Modo de busca (default: hybrid)"},"generate":{"type":"boolean","description":"Sintetizar resposta via LLM configurado no servidor (default: true quando Chat:Provider configurado)"}},"required":["question"],"examples":[{"question":"Como funciona a sincronização?","topK":5,"generate":true}]}""",
        ["agent_chat"] = """{"type":"object","properties":{"prompt":{"type":"string","description":"Pergunta/tarefa em linguagem natural — o agente itera tools até responder","examples":["Resuma as notas da semana"]},"tools":{"type":"array","items":{"type":"string"},"description":"Allowlist de tools expostas ao modelo (default: todas as read-only)","examples":[["search_knowledge","ask_knowledge"]]},"maxIterations":{"type":"integer","description":"Teto de iterações model→tools→model (default 10)"},"allowWrite":{"type":"boolean","description":"Opt-in: expõe tools de escrita (write_knowledge, write_note)"},"threadId":{"type":"string","description":"GUID de thread existente — continua a conversa com contexto"},"persist":{"type":"boolean","description":"Cria thread nova e persiste os turnos desta chamada"}},"required":["prompt"],"examples":[{"prompt":"Resuma as notas da semana","tools":["search_knowledge","ask_knowledge"],"maxIterations":10,"persist":true}]}""",
        ["write_knowledge"] = """{"type":"object","properties":{"title":{"type":"string","description":"Título do documento (vira nome de arquivo em vaults)","examples":["Nota de exemplo"]},"content":{"type":"string","description":"Conteúdo em markdown/texto","examples":["# Título\n\nConteúdo em markdown."]},"source":{"type":"string","description":"Slug da fonte alvo (default: primeira ativa)"},"tags":{"type":"array","items":{"type":"string"},"description":"Tags (frontmatter em vaults)","examples":[["exemplo"]]}},"required":["title","content"],"examples":[{"title":"Nota de exemplo","content":"# Título\n\nConteúdo em markdown.","tags":["exemplo"]}]}""",
        ["read_document"] = """{"type":"object","properties":{"path":{"type":"string","description":"Caminho relativo da nota dentro do vault","examples":["pasta/nota.md"]},"source":{"type":"string","description":"Slug do vault (default: primeiro vault ativo)"}},"required":["path"],"examples":[{"path":"pasta/nota.md"}]}""",
        ["write_note"] = """{"type":"object","properties":{"path":{"type":"string","description":"Caminho relativo da nota (.md é acrescentado se ausente)","examples":["diario/2026-09-14"]},"content":{"type":"string","description":"Conteúdo markdown","examples":["# Nota\n\nTexto."]},"tags":{"type":"array","items":{"type":"string"},"description":"Tags → frontmatter","examples":[["diario"]]},"source":{"type":"string","description":"Slug do vault (default: primeiro vault ativo)"}},"required":["path","content"],"examples":[{"path":"diario/2026-09-14","content":"# Nota\n\nTexto.","tags":["diario"]}]}""",
        ["ask_question"] = """{"type":"object","properties":{"repoName":{"anyOf":[{"type":"string"},{"type":"array","items":{"type":"string"},"maxItems":10}],"description":"GitHub repo(s) in owner/repo format (max 10)","examples":["langchain-ai/langgraph"]},"question":{"type":"string","description":"Question about the repository","examples":["How does checkpointing work?"]}},"required":["repoName","question"],"examples":[{"repoName":"langchain-ai/langgraph","question":"How does checkpointing work?"},{"repoName":["langchain-ai/langgraph","afonsoft/skills"],"question":"Compare the architectures"}]}""",
        ["read_wiki_structure"] = """{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repo in owner/repo format","examples":["langchain-ai/langgraph"]}},"required":["repoName"],"examples":[{"repoName":"langchain-ai/langgraph"}]}""",
        ["read_wiki_contents"] = """{"type":"object","properties":{"repoName":{"type":"string","description":"GitHub repo in owner/repo format","examples":["langchain-ai/langgraph"]}},"required":["repoName"],"examples":[{"repoName":"langchain-ai/langgraph"}]}""",
        // SPEC-20260916-firecrawl-mcp-proxy: static Firecrawl core. With a real
        // key, upstream tools/list merges in dynamically — this fixture is
        // keyless, so only the static surface is pinned here.
        ["firecrawl_scrape"] = """{"type":"object","additionalProperties":true,"properties":{"url":{"type":"string","description":"URL to scrape","examples":["https://example.com"]},"formats":{"type":"array","items":{"type":"string"},"description":"Output formats (e.g. markdown, html, json)"},"onlyMainContent":{"type":"boolean","description":"Exclude nav/footer boilerplate"}},"required":["url"],"examples":[{"url":"https://example.com"},{"url":"https://docs.firecrawl.dev","formats":["markdown"],"onlyMainContent":true}]}""",
        ["firecrawl_search"] = """{"type":"object","additionalProperties":true,"properties":{"query":{"type":"string","description":"Search query","examples":["latest .NET 10 release notes"]},"limit":{"type":"integer","description":"Max results"}},"required":["query"],"examples":[{"query":"latest .NET 10 release notes"}]}""",
        ["firecrawl_map"] = """{"type":"object","additionalProperties":true,"properties":{"url":{"type":"string","description":"Site URL to map","examples":["https://docs.firecrawl.dev"]}},"required":["url"],"examples":[{"url":"https://docs.firecrawl.dev"}]}""",
        ["firecrawl_crawl"] = """{"type":"object","additionalProperties":true,"properties":{"url":{"type":"string","description":"Starting URL","examples":["https://docs.firecrawl.dev"]},"limit":{"type":"integer","description":"Max pages to crawl"}},"required":["url"],"examples":[{"url":"https://docs.firecrawl.dev","limit":5}]}""",
        ["firecrawl_check_crawl_status"] = """{"type":"object","additionalProperties":true,"properties":{"id":{"type":"string","description":"Crawl job id","examples":["550e8400-e29b-41d4-a716-446655440000"]}},"required":["id"],"examples":[{"id":"550e8400-e29b-41d4-a716-446655440000"}]}""",
        ["firecrawl_parse"] = """{"type":"object","additionalProperties":true,"properties":{"url":{"type":"string","description":"Public URL of the document to parse","examples":["https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf"]},"filePath":{"type":"string","description":"Local file path (two-phase upload flow)"}},"examples":[{"url":"https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf"}]}""",
        // SPEC-20260916-api-key-settings: per-API-key settings (chat + integrations).
        ["set_api_key_settings"] = """{"type":"object","properties":{"provider":{"type":"string","enum":["chat","firecrawl","deepwiki","tavily","context7"],"description":"Which provider to configure"},"endpoint":{"type":["string","null"],"description":"OpenAI-compatible base URL (chat only, null = inherit)"},"model":{"type":["string","null"],"description":"Model name (chat only, null = inherit)"},"apiKey":{"type":["string","null"],"description":"API key override (null = inherit from global)"}},"required":["provider"],"examples":[{"provider":"chat","endpoint":"http://localhost:11434","model":"llama3","apiKey":null}]}""",
        // SPEC-20260916-tavily-mcp-proxy: static Tavily core (keyless fixture —
        // only the static surface is pinned; upstream merge needs a real key).
        ["tavily_search"] = """{"type":"object","additionalProperties":true,"properties":{"query":{"type":"string","description":"Search query","examples":["latest .NET 10 release notes"]},"max_results":{"type":"integer","description":"Max results (default 5)"},"search_depth":{"type":"string","description":"basic | advanced"},"include_domains":{"type":"array","items":{"type":"string"}},"exclude_domains":{"type":"array","items":{"type":"string"}}},"required":["query"],"examples":[{"query":"latest .NET 10 release notes"},{"query":"site reliability best practices","max_results":3,"search_depth":"basic"}]}""",
        ["tavily_extract"] = """{"type":"object","additionalProperties":true,"properties":{"urls":{"type":"array","items":{"type":"string"},"description":"One or more URLs to extract content from","examples":[["https://example.com"]]}},"required":["urls"],"examples":[{"urls":["https://example.com"]}]}""",
        ["tavily_map"] = """{"type":"object","additionalProperties":true,"properties":{"url":{"type":"string","description":"Site URL to map","examples":["https://docs.tavily.com"]},"max_depth":{"type":"integer","description":"Max link depth"},"limit":{"type":"integer","description":"Max URLs returned"}},"required":["url"],"examples":[{"url":"https://docs.tavily.com","max_depth":1,"limit":20}]}""",
        ["tavily_crawl"] = """{"type":"object","additionalProperties":true,"properties":{"url":{"type":"string","description":"Starting URL","examples":["https://docs.tavily.com"]},"max_depth":{"type":"integer","description":"Max link depth"},"limit":{"type":"integer","description":"Max pages to crawl"},"instructions":{"type":"string","description":"Natural-language crawl guidance"}},"required":["url"],"examples":[{"url":"https://docs.tavily.com","max_depth":1,"limit":5}]}""",
        ["tavily_research"] = """{"type":"object","additionalProperties":true,"properties":{"input":{"type":"string","description":"Research question or task","examples":["Compare .NET 10 minimal APIs vs controllers for a small team"]},"model":{"type":"string","description":"Research model (e.g. mini | pro)"}},"required":["input"],"examples":[{"input":"Compare .NET 10 minimal APIs vs controllers for a small team"}]}""",
        // SPEC-20260922-context7-mcp-proxy: static Context7 core (keyless
        // fixture — only the static surface is pinned; upstream merge needs a
        // real key).
        ["resolve-library-id"] = """{"type":"object","additionalProperties":true,"properties":{"query":{"type":"string","description":"What to look up in the library's documentation — used to rank results by relevance","examples":["app router routing"]},"libraryName":{"type":"string","description":"Official library name (e.g. 'Next.js', 'MongoDB', 'Three.js')","examples":["Next.js"]}},"required":["query","libraryName"],"examples":[{"libraryName":"Next.js","query":"app router routing"},{"libraryName":"MongoDB","query":"connection pooling"}]}""",
        ["query-docs"] = """{"type":"object","additionalProperties":true,"properties":{"libraryId":{"type":"string","description":"Exact Context7 library ID (/org/project[/version]) — from resolve-library-id or the user query","examples":["/vercel/next.js"]},"query":{"type":"string","description":"Single-concept question to look up in the library docs","examples":["middleware authentication"]}},"required":["libraryId","query"],"examples":[{"libraryId":"/vercel/next.js","query":"middleware authentication"},{"libraryId":"/mongodb/docs","query":"connection string format"}]}""",
        // query_{slug} tools share this schema (SourceQueryToolsProvider).
        ["__query_source__"] = """{"type":"object","properties":{"query":{"type":"string","description":"Texto ou pergunta a buscar nesta fonte","examples":["termo de busca"]},"topK":{"type":"integer","description":"Máx. de resultados (default 5, máx 50)"}},"required":["query"],"examples":[{"query":"termo de busca","topK":5}]}""",
    };

    private static readonly HashSet<string> WriteTools =
        ["write_knowledge", "write_note", "firecrawl_crawl", "tavily_crawl", "tavily_research", "set_api_key_settings"];

    [Fact]
    public async Task ToolCatalog_NamesSchemasAndHints_MatchPinnedContract()
    {
        var mcp = await ConnectAsync(_factory);
        var http = await TestAuth.LoginAsync(_factory);

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
