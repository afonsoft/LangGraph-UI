using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace KnowledgeHub.Tests.Integration;

// Covers SPEC-01 acceptance criteria: legacy SSE handshake, Streamable HTTP init, expired session.
public class McpTransportTests : IClassFixture<McpTransportTests.Fixture>
{
    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"kh-mcp-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Path"] = DbPath }));
        }
    }

    private readonly Fixture _factory;

    public McpTransportTests(Fixture factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static StringContent InitializeRequest() => new(
        """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}
        """,
        Encoding.UTF8, "application/json");

    [Fact]
    public async Task Get_McpSse_EmitsEndpointEvent_WithSessionId()
    {
        // AC: GET /mcp/sse → first frame is `event: endpoint` carrying sessionId
        var client = await TestAuth.LoginAsync(_factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await client.GetAsync("/mcp/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var buffer = new byte[2048];
        var read = await stream.ReadAsync(buffer, cts.Token);
        var frame = Encoding.UTF8.GetString(buffer, 0, read);

        Assert.Contains("event: endpoint", frame);
        Assert.Contains("sessionId=", frame);
    }

    [Fact]
    public async Task Post_Mcp_StreamableHttp_Initialize_ReturnsServerInfo_AndSessionHeader()
    {
        // AC: POST initialize → /mcp returns valid result + Mcp-Session-Id header (stateful)
        var client = await TestAuth.LoginAsync(_factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = InitializeRequest() };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("Mcp-Session-Id"));

        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Contains("knowledge", body);
        Assert.Contains("protocolVersion", body);
        Assert.Contains("tools", body);
    }

    [Fact]
    public async Task Post_McpMessage_WithUnknownSession_Fails()
    {
        // AC: expired/unknown sessionId → HTTP error (not a silent accept)
        var client = await TestAuth.LoginAsync(_factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await client.PostAsync("/mcp/message?sessionId=definitely-not-a-session", InitializeRequest(), cts.Token);
        Assert.True((int)response.StatusCode >= 400, $"expected error, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Post_Mcp_WithMalformedJson_ReturnsJsonRpcError()
    {
        // Edge case: malformed JSON → -32700 parse error from the SDK
        var client = await TestAuth.LoginAsync(_factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{invalid", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        // SDK 2.x rejects unparseable bodies with -32600 (Bad Request) rather than -32700.
        Assert.Contains("\"error\"", body);
        Assert.Matches(@"""code"":\s*-32(600|700)", body);
    }
}
