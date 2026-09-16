using KnowledgeHub.Server.Mcp.Upstream;
using KnowledgeHub.Server.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260916-firecrawl-mcp-proxy RF-004/RF-008: credential
// resolution precedence (store → env) and DeepWiki public/private endpoint
// switching.
public class UpstreamCredentialTests
{
    private static DeepWikiUpstreamClient DeepWiki(
        string? storedKey = null, string envKey = "", string endpoint = "https://mcp.deepwiki.com/mcp",
        string privateEndpoint = "https://mcp.devin.ai/mcp") =>
        new(Options.Create(new DeepWikiOptions
        {
            ApiKey = envKey,
            Endpoint = endpoint,
            PrivateEndpoint = privateEndpoint
        }),
            new FakeSecretStore(deepwikiKey: storedKey),
            NullLoggerFactory.Instance,
            NullLogger<DeepWikiUpstreamClient>.Instance);

    private static FirecrawlUpstreamClient Firecrawl(string? storedKey = null, string envKey = "") =>
        new(Options.Create(new FirecrawlOptions { ApiKey = envKey }),
            new FakeSecretStore(firecrawlKey: storedKey),
            NullLoggerFactory.Instance,
            NullLogger<FirecrawlUpstreamClient>.Instance);

    private static TavilyUpstreamClient Tavily(string? storedKey = null, string envKey = "") =>
        new(Options.Create(new TavilyOptions { ApiKey = envKey }),
            new FakeSecretStore(tavilyKey: storedKey),
            NullLoggerFactory.Instance,
            NullLogger<TavilyUpstreamClient>.Instance);

    [Fact]
    public async Task DeepWiki_ResolveKey_StoreBeatsEnv()
    {
        var client = DeepWiki(storedKey: "dw-store", envKey: "dw-env");
        Assert.Equal("dw-store", await client.ResolveApiKeyAsync());
    }

    [Fact]
    public async Task DeepWiki_ResolveKey_EnvFallback()
    {
        var client = DeepWiki(envKey: "dw-env");
        Assert.Equal("dw-env", await client.ResolveApiKeyAsync());
    }

    [Fact]
    public async Task DeepWiki_ResolveKey_None()
    {
        var client = DeepWiki();
        Assert.Null(await client.ResolveApiKeyAsync());
        Assert.False(await client.HasApiKeyAsync());
    }

    [Fact]
    public void DeepWiki_Endpoint_NoKey_Public()
    {
        var client = DeepWiki();
        Assert.Equal(new Uri("https://mcp.deepwiki.com/mcp"), client.EffectiveEndpoint(null));
        Assert.Equal(new Uri("https://mcp.deepwiki.com/mcp"), client.EffectiveEndpoint(""));
    }

    [Fact]
    public void DeepWiki_Endpoint_WithKey_Private()
    {
        var client = DeepWiki();
        Assert.Equal(new Uri("https://mcp.devin.ai/mcp"), client.EffectiveEndpoint("dw-1"));
    }

    [Fact]
    public void DeepWiki_TransportOptions_WithKey_PrivateEndpointPlusBearer()
    {
        var client = DeepWiki();
        var options = client.CreateTransportOptions("dw-secret");

        Assert.Equal(new Uri("https://mcp.devin.ai/mcp"), options.Endpoint);
        Assert.Equal("Bearer dw-secret", options.AdditionalHeaders?["Authorization"]);
    }

    [Fact]
    public void DeepWiki_TransportOptions_NoKey_PublicEndpointNoAuth()
    {
        var client = DeepWiki();
        var options = client.CreateTransportOptions(null);

        Assert.Equal(new Uri("https://mcp.deepwiki.com/mcp"), options.Endpoint);
        Assert.False(options.AdditionalHeaders?.ContainsKey("Authorization") ?? false);
    }

    [Fact]
    public async Task Firecrawl_ResolveKey_StoreBeatsEnv()
    {
        var client = Firecrawl(storedKey: "fc-store", envKey: "fc-env");
        Assert.Equal("fc-store", await client.ResolveApiKeyAsync());
    }

    [Fact]
    public async Task Firecrawl_ResolveKey_EnvFallback_And_None()
    {
        Assert.Equal("fc-env", await Firecrawl(envKey: "fc-env").ResolveApiKeyAsync());
        Assert.Null(await Firecrawl().ResolveApiKeyAsync());
    }

    [Fact]
    public void Firecrawl_TransportOptions_BearerOnlyWhenKeyed()
    {
        var keyed = Firecrawl().CreateTransportOptions("fc-abc");
        Assert.Equal("Bearer fc-abc", keyed.AdditionalHeaders?["Authorization"]);
        Assert.Equal(new Uri("https://mcp.firecrawl.dev/v2/mcp"), keyed.Endpoint);

        var keyless = Firecrawl().CreateTransportOptions(null);
        Assert.False(keyless.AdditionalHeaders?.ContainsKey("Authorization") ?? false);
    }

    [Fact]
    public async Task Tavily_ResolveKey_StoreBeatsEnv()
    {
        var client = Tavily(storedKey: "tvly-store", envKey: "tvly-env");
        Assert.Equal("tvly-store", await client.ResolveApiKeyAsync());
    }

    [Fact]
    public async Task Tavily_ResolveKey_EnvFallback_And_None()
    {
        Assert.Equal("tvly-env", await Tavily(envKey: "tvly-env").ResolveApiKeyAsync());
        Assert.Null(await Tavily().ResolveApiKeyAsync());
    }

    [Fact]
    public void Tavily_TransportOptions_BearerOnlyWhenKeyed_NeverInUrl()
    {
        var keyed = Tavily().CreateTransportOptions("tvly-abc");
        Assert.Equal("Bearer tvly-abc", keyed.AdditionalHeaders?["Authorization"]);
        Assert.Equal(new Uri("https://mcp.tavily.com/mcp"), keyed.Endpoint);
        Assert.DoesNotContain("tavilyApiKey", keyed.Endpoint.Query, StringComparison.OrdinalIgnoreCase);

        var keyless = Tavily().CreateTransportOptions(null);
        Assert.False(keyless.AdditionalHeaders?.ContainsKey("Authorization") ?? false);
    }

    private sealed class FakeSecretStore(string? firecrawlKey = null, string? deepwikiKey = null, string? tavilyKey = null)
        : IIntegrationSecretStore
    {
        public Task<string?> GetAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(provider == IntegrationProviders.Firecrawl ? firecrawlKey
                : provider == IntegrationProviders.DeepWiki ? deepwikiKey
                : provider == IntegrationProviders.Tavily ? tavilyKey
                : (string?)null);

        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);

        public Task SetAsync(string provider, string secret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> RemoveAsync(string provider, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
