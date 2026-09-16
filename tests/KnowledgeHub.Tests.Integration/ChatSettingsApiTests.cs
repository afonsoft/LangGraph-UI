using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeHub.Tests.Integration;

// SPEC-20260916-settings-chat-config RF-004/RF-005: /api/settings/chat endpoints —
// auth, validation, masked key reporting, store→env reset, and the connection
// test (probe stubbed via the Func<HttpClient> seam).
public class ChatSettingsApiTests : IClassFixture<ChatSettingsApiTests.Fixture>
{
    private const string StoredKey = "sk-chat-settings-9e5t";

    public sealed class Fixture : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = Path.Combine(Path.GetTempPath(), $"kh-chatsettings-{Guid.NewGuid():N}.db")
                }));
            builder.ConfigureServices(services =>
                services.AddSingleton<Func<HttpClient>>(() =>
                    new HttpClient(new StubModelsHandler())));
        }
    }

    /// <summary>Answers the /v1/models probe with a fixed catalog.</summary>
    private sealed class StubModelsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"id":"stub-model"}]}""", Encoding.UTF8, "application/json")
            });
    }

    private readonly Fixture _factory;
    public ChatSettingsApiTests(Fixture factory) => _factory = factory;

    /// <summary>Shared fixture DB — resets chat settings so tests are order-independent.</summary>
    private async Task<HttpClient> AuthedCleanAsync()
    {
        var http = await TestAuth.LoginAsync(_factory);
        (await http.DeleteAsync("/api/settings/chat")).EnsureSuccessStatusCode();
        return http;
    }

    [Fact]
    public async Task Anonymous_Get_Returns401()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/settings/chat")).StatusCode);
    }

    [Fact]
    public async Task Get_NoConfig_SourceNone()
    {
        var http = await AuthedCleanAsync();
        using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/settings/chat"));

        Assert.Equal("none", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal("none", doc.RootElement.GetProperty("provider").GetString());
        Assert.False(doc.RootElement.GetProperty("hasApiKey").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("envConfigured").GetBoolean());
    }

    [Fact]
    public async Task Put_InvalidEndpoint_400()
    {
        var http = await AuthedCleanAsync();
        var put = await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "not-a-uri", model = "m" });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_MissingModel_400()
    {
        var http = await AuthedCleanAsync();
        var put = await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "https://chat.test", model = "" });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_ThenGet_StoreSource_MaskedKey_NeverEchoed()
    {
        var http = await AuthedCleanAsync();

        var put = await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "https://chat.test", model = "deepseek", apiKey = StoredKey });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var body = await http.GetStringAsync("/api/settings/chat");
        Assert.DoesNotContain(StoredKey, body); // secret never leaves the server

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("openai", root.GetProperty("provider").GetString());
        Assert.Equal("store", root.GetProperty("source").GetString());
        Assert.Equal("https://chat.test", root.GetProperty("endpoint").GetString());
        Assert.Equal("deepseek", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("store", root.GetProperty("apiKeySource").GetString());
        Assert.Equal("••••9e5t", root.GetProperty("apiKeyHint").GetString());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Put_BlankApiKey_KeepsStoredKey()
    {
        var http = await AuthedCleanAsync();
        await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "https://chat.test", model = "m1", apiKey = StoredKey });

        var put = await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "https://chat2.test", model = "m2" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/settings/chat"));
        Assert.Equal("https://chat2.test", doc.RootElement.GetProperty("endpoint").GetString());
        Assert.True(doc.RootElement.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("store", doc.RootElement.GetProperty("apiKeySource").GetString());
    }

    [Fact]
    public async Task Delete_ApiKey_RemovesKeyOnly()
    {
        var http = await AuthedCleanAsync();
        await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "https://chat.test", model = "m", apiKey = StoredKey });

        Assert.Equal(HttpStatusCode.NoContent,
            (await http.DeleteAsync("/api/settings/chat/apikey")).StatusCode);

        using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/settings/chat"));
        Assert.False(doc.RootElement.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("none", doc.RootElement.GetProperty("apiKeySource").GetString());
        Assert.Equal("store", doc.RootElement.GetProperty("source").GetString()); // endpoint/model persist
    }

    [Fact]
    public async Task Delete_Chat_ResetsToEnv()
    {
        var http = await AuthedCleanAsync();
        await http.PutAsJsonAsync("/api/settings/chat",
            new { endpoint = "https://chat.test", model = "m", apiKey = StoredKey });

        Assert.Equal(HttpStatusCode.NoContent,
            (await http.DeleteAsync("/api/settings/chat")).StatusCode);

        using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/settings/chat"));
        Assert.Equal("none", doc.RootElement.GetProperty("source").GetString());
        Assert.False(doc.RootElement.GetProperty("hasApiKey").GetBoolean());
    }

    [Fact]
    public async Task Test_NoEndpoint_OkFalse()
    {
        var http = await AuthedCleanAsync();
        var response = await http.PostAsJsonAsync("/api/settings/chat/test", new { });
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Test_InvalidEndpoint_400()
    {
        var http = await AuthedCleanAsync();
        var response = await http.PostAsJsonAsync("/api/settings/chat/test",
            new { endpoint = "bad-endpoint" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Test_StubbedProbe_ModelListedFlag()
    {
        var http = await AuthedCleanAsync();

        var listed = await http.PostAsJsonAsync("/api/settings/chat/test",
            new { endpoint = "https://stub.local", model = "stub-model", apiKey = "k" });
        using var doc1 = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.True(doc1.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc1.RootElement.GetProperty("modelListed").GetBoolean());

        var absent = await http.PostAsJsonAsync("/api/settings/chat/test",
            new { endpoint = "https://stub.local", model = "absent-model", apiKey = "k" });
        using var doc2 = JsonDocument.Parse(await absent.Content.ReadAsStringAsync());
        Assert.True(doc2.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(doc2.RootElement.GetProperty("modelListed").GetBoolean());
    }
}
