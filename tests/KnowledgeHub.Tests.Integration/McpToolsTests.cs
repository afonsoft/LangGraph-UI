using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KnowledgeHub.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace KnowledgeHub.Tests.Integration;

// Covers SPEC-04 ACs: dynamic tools/list, tools/call execution, catalog reactivity.
public class McpToolsTests : IClassFixture<McpToolsTests.Fixture>
{
    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"kh-tools-{Guid.NewGuid():N}.db");
        public string Vault { get; } = Path.Combine(Path.GetTempPath(), $"vault-tools-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Vault);
            File.WriteAllText(Path.Combine(Vault, "nota.md"),
                "# Nota de Teste\n\nconteúdo sobre embeddings vetores e busca semântica profunda");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = DbPath
                }));
        }
    }

    private readonly Fixture _factory;

    public McpToolsTests(Fixture factory) => _factory = factory;

    /// <summary>Minimal MCP client over Streamable HTTP (/mcp).</summary>
    private sealed class McpTestClient : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private string? _sessionId;
        private int _nextId = 1;

        public McpTestClient(HttpClient http) => _http = http;

        public async Task<JsonElement> SendAsync(string method, object? parameters = null)
        {
            var id = _nextId++;
            var body = parameters is null
                ? $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}"}"""
                : $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{JsonSerializer.Serialize(parameters)}}}""";

            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (_sessionId is not null)
            {
                request.Headers.Add("Mcp-Session-Id", _sessionId);
                request.Headers.Add("MCP-Protocol-Version", "2025-03-26");
            }

            using var response = await _http.SendAsync(request);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var ids))
                _sessionId = ids.First();
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync();
            var data = ExtractLastMessage(payload);
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("error", out var err))
                throw new InvalidOperationException($"JSON-RPC error: {err.GetRawText()}");
            return doc.RootElement.GetProperty("result").Clone();
        }

        public async Task NotifyAsync(string method)
        {
            var body = $$"""{"jsonrpc":"2.0","method":"{{method}}"}""";
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (_sessionId is not null)
            {
                request.Headers.Add("Mcp-Session-Id", _sessionId);
                request.Headers.Add("MCP-Protocol-Version", "2025-03-26");
            }
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private static string ExtractLastMessage(string payload)
        {
            // Streamable HTTP may frame the response as SSE `event: message` blocks.
            if (!payload.Contains("data:"))
                return payload;
            string? last = null;
            foreach (var line in payload.Split('\n'))
                if (line.StartsWith("data:"))
                    last = line[5..].Trim();
            return last ?? payload;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private async Task<McpTestClient> ConnectAsync()
    {
        var client = new McpTestClient(_factory.CreateClient());
        var init = await client.SendAsync("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
        });
        Assert.Equal("knowledge-hub", init.GetProperty("serverInfo").GetProperty("name").GetString());
        await client.NotifyAsync("notifications/initialized");
        return client;
    }

    private static HashSet<string> ToolNames(JsonElement toolsList) =>
        toolsList.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToHashSet();

    [Fact]
    public async Task ToolsList_AlwaysExposes_CoreTools()
    {
        var mcp = await ConnectAsync();
        var tools = await mcp.SendAsync("tools/list");
        var names = ToolNames(tools);

        Assert.Contains("search_knowledge", names);
        Assert.Contains("ask_knowledge", names);
        Assert.Contains("write_knowledge", names);
    }

    [Fact]
    public async Task ToolsList_ReflectsSourceActivation()
    {
        var mcp = await ConnectAsync();
        var http = _factory.CreateClient();

        var name = $"XVault{Guid.NewGuid():N}";
        var create = await http.PostAsJsonAsync("/api/sources", new
        {
            name,
            type = "ObsidianVault",
            configuration = new { path = _factory.Vault },
            isActive = true
        });
        create.EnsureSuccessStatusCode();
        var source = (await create.Content.ReadFromJsonAsync<KnowledgeSourceDto>())!;

        var after = ToolNames(await mcp.SendAsync("tools/list"));
        Assert.Contains($"query_{name.ToLowerInvariant()}", after);
        Assert.Contains("read_document", after);
        Assert.Contains("write_note", after);

        await http.PostAsync($"/api/sources/{source.Id}/deactivate", null);
        var deactivated = ToolNames(await mcp.SendAsync("tools/list"));
        Assert.DoesNotContain($"query_{name.ToLowerInvariant()}", deactivated);
    }

    [Fact]
    public async Task ToolsCall_SearchKnowledge_ReturnsRankedHits()
    {
        var mcp = await ConnectAsync();
        var http = _factory.CreateClient();

        var create = await http.PostAsJsonAsync("/api/sources", new
        {
            name = $"vault-{Guid.NewGuid():N}",
            type = "ObsidianVault",
            configuration = new { path = _factory.Vault }
        });
        var source = (await create.Content.ReadFromJsonAsync<KnowledgeSourceDto>())!;
        await http.PostAsync($"/api/sources/{source.Id}/sync", null);

        var result = await mcp.SendAsync("tools/call", new
        {
            name = "search_knowledge",
            arguments = new { query = "embeddings vetores", topK = 3 }
        });

        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("Nota de Teste", text);
        Assert.False(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_IsProtocolError()
    {
        var mcp = await ConnectAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mcp.SendAsync("tools/call", new { name = "nonexistent_tool", arguments = new { } }));
        Assert.Contains("-32601", ex.Message);
    }

    [Fact]
    public async Task ToolsCall_MissingRequiredArg_IsInvalidParams()
    {
        // SPEC-04: invalid params → McpProtocolException(InvalidParams) → JSON-RPC -32602
        var mcp = await ConnectAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mcp.SendAsync("tools/call", new { name = "search_knowledge", arguments = new { } }));
        Assert.Contains("-32602", ex.Message);
        Assert.Contains("query", ex.Message);
    }

    [Fact]
    public async Task ToolsCall_ReadDocument_TraversalRejected()
    {
        var mcp = await ConnectAsync();
        var http = _factory.CreateClient();
        await http.PostAsJsonAsync("/api/sources", new
        {
            name = $"v-{Guid.NewGuid():N}",
            type = "ObsidianVault",
            configuration = new { path = _factory.Vault }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mcp.SendAsync("tools/call", new
            {
                name = "read_document",
                arguments = new { path = "../outside.md" }
            }));
        Assert.Contains("-32602", ex.Message);
    }

    [Fact]
    public async Task ResourcesList_AndRead_Catalog()
    {
        var mcp = await ConnectAsync();
        var list = await mcp.SendAsync("resources/list");
        var uris = list.GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("uri").GetString()!).ToList();
        Assert.Contains("knowledge://sources", uris);

        var read = await mcp.SendAsync("resources/read", new { uri = "knowledge://sources" });
        var text = read.GetProperty("contents")[0].GetProperty("text").GetString();
        Assert.Contains("\"Name\"", text);
    }
}
