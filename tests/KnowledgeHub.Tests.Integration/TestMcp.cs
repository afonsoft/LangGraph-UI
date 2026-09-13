using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KnowledgeHub.Tests.Integration;

/// <summary>Minimal MCP client over Streamable HTTP (/mcp) shared by integration tests.</summary>
public sealed class TestMcp : IAsyncDisposable
{
    private readonly HttpClient _http;
    private string? _sessionId;
    private int _nextId = 1;

    public TestMcp(HttpClient http) => _http = http;

    public static async Task<TestMcp> ConnectAsync(WebApplicationFactory<Program> factory)
    {
        var client = new TestMcp(factory.CreateClient());
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

    public static HashSet<string> ToolNames(JsonElement toolsList) =>
        toolsList.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToHashSet();

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
