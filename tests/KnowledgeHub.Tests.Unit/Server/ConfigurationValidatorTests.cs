using KnowledgeHub.Server.Configuration;
using Microsoft.Extensions.Configuration;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260914-config-validation: fail-fast startup validation of
// Embeddings / VectorStore / DeepWiki config blocks.
public class ConfigurationValidatorTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Valid_MinimalConfig_Passes()
    {
        // Covers AC: valid config produces no noise
        var cfg = Config(new Dictionary<string, string?>());
        ConfigurationValidator.Validate(cfg); // must not throw — all defaults valid
    }

    [Fact]
    public void Invalid_EmbeddingProvider_Fails()
    {
        var cfg = Config(new() { ["Embeddings:Provider"] = "bogus" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Embeddings:Provider", ex.Message);
    }

    [Fact]
    public void Ollama_WithoutEndpoint_Fails()
    {
        var cfg = Config(new()
        {
            ["Embeddings:Provider"] = "ollama",
            ["Embeddings:Model"] = "nomic-embed-text"
        });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Embeddings:Endpoint", ex.Message);
    }

    [Fact]
    public void Ollama_WithValidEndpoint_Passes()
    {
        var cfg = Config(new()
        {
            ["Embeddings:Provider"] = "ollama",
            ["Embeddings:Endpoint"] = "http://localhost:11434",
            ["Embeddings:Model"] = "nomic-embed-text"
        });
        ConfigurationValidator.Validate(cfg);
    }

    [Fact]
    public void Invalid_Dimensions_Fails()
    {
        var cfg = Config(new() { ["Embeddings:Dimensions"] = "-5" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Embeddings:Dimensions", ex.Message);
    }

    [Fact]
    public void Postgres_WithoutConnectionString_Fails()
    {
        var cfg = Config(new() { ["VectorStore:Provider"] = "postgres" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("VectorStore:ConnectionString", ex.Message);
    }

    [Fact]
    public void Invalid_VectorStoreProvider_Fails()
    {
        var cfg = Config(new() { ["VectorStore:Provider"] = "qdrant" });
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
    }

    [Fact]
    public void Invalid_DeepWikiEndpoint_Fails()
    {
        var cfg = Config(new() { ["DeepWiki:Endpoint"] = "not-a-url" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("DeepWiki:Endpoint", ex.Message);
    }

    [Fact]
    public void Invalid_DeepWikiPrivateEndpoint_Fails()
    {
        var cfg = Config(new() { ["DeepWiki:PrivateEndpoint"] = "not-a-url" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("DeepWiki:PrivateEndpoint", ex.Message);
    }

    [Fact]
    public void Invalid_FirecrawlEndpoint_Fails()
    {
        var cfg = Config(new() { ["Firecrawl:Endpoint"] = "not-a-url" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Firecrawl:Endpoint", ex.Message);
    }

    [Fact]
    public void Firecrawl_Disabled_SkipsValidation()
    {
        var cfg = Config(new()
        {
            ["Firecrawl:Enabled"] = "false",
            ["Firecrawl:Endpoint"] = "not-a-url",
            ["Firecrawl:TimeoutSeconds"] = "-1"
        });
        ConfigurationValidator.Validate(cfg);
    }

    [Theory]
    [InlineData("Firecrawl:TimeoutSeconds")]
    [InlineData("Firecrawl:ToolsCacheSeconds")]
    public void Firecrawl_InvalidPositiveInt_Fails(string key)
    {
        var cfg = Config(new() { [key] = "0" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Invalid_TavilyEndpoint_Fails()
    {
        var cfg = Config(new() { ["Tavily:Endpoint"] = "not-a-url" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Tavily:Endpoint", ex.Message);
    }

    [Fact]
    public void Tavily_Disabled_SkipsValidation()
    {
        var cfg = Config(new()
        {
            ["Tavily:Enabled"] = "false",
            ["Tavily:Endpoint"] = "not-a-url",
            ["Tavily:TimeoutSeconds"] = "-1"
        });
        ConfigurationValidator.Validate(cfg);
    }

    [Theory]
    [InlineData("Tavily:TimeoutSeconds")]
    [InlineData("Tavily:ToolsCacheSeconds")]
    public void Tavily_InvalidPositiveInt_Fails(string key)
    {
        var cfg = Config(new() { [key] = "0" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void DeepWiki_Disabled_SkipsEndpointValidation()
    {
        var cfg = Config(new()
        {
            ["DeepWiki:Enabled"] = "false",
            ["DeepWiki:Endpoint"] = "not-a-url"
        });
        ConfigurationValidator.Validate(cfg);
    }

    [Fact]
    public void Chat_None_Passes()
    {
        var cfg = Config(new() { ["Chat:Provider"] = "none" });
        ConfigurationValidator.Validate(cfg);
    }

    [Fact]
    public void Chat_InvalidProvider_Fails()
    {
        var cfg = Config(new() { ["Chat:Provider"] = "anthropic-x" });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Chat:Provider", ex.Message);
    }

    [Fact]
    public void Chat_OllamaWithoutEndpoint_Fails()
    {
        var cfg = Config(new()
        {
            ["Chat:Provider"] = "ollama",
            ["Chat:Model"] = "llama3"
        });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Chat:Endpoint", ex.Message);
    }

    [Fact]
    public void Chat_OpenAiValid_Passes()
    {
        var cfg = Config(new()
        {
            ["Chat:Provider"] = "openai",
            ["Chat:Endpoint"] = "https://api.openai.com",
            ["Chat:Model"] = "gpt-4o-mini",
            ["Chat:TimeoutSeconds"] = "60"
        });
        ConfigurationValidator.Validate(cfg);
    }

    [Fact]
    public void Chat_InvalidTimeout_Fails()
    {
        var cfg = Config(new()
        {
            ["Chat:Provider"] = "ollama",
            ["Chat:Endpoint"] = "http://localhost:11434",
            ["Chat:Model"] = "llama3",
            ["Chat:TimeoutSeconds"] = "0"
        });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Chat:TimeoutSeconds", ex.Message);
    }

    [Fact]
    public void MultipleProblems_AllReported()
    {
        var cfg = Config(new()
        {
            ["Embeddings:Provider"] = "bogus",
            ["VectorStore:Provider"] = "qdrant"
        });
        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(cfg));
        Assert.Contains("Embeddings:Provider", ex.Message);
        Assert.Contains("VectorStore:Provider", ex.Message);
    }

    // SPEC-20260916-redis-exposure-risk RF-002: redis provider without
    // password= in the connection string → startup warning, never a failure.
    [Fact]
    public void Redis_WithoutPassword_Warns()
    {
        var cfg = Config(new()
        {
            ["Cache:Provider"] = "redis",
            ["Cache:Redis:ConnectionString"] = "host.docker.internal:6379,defaultDatabase=3"
        });
        var warnings = ConfigurationValidator.CollectWarnings(cfg);
        var warning = Assert.Single(warnings);
        Assert.Contains("Redis", warning);
        ConfigurationValidator.Validate(cfg); // warning only — must not throw
    }

    [Fact]
    public void Redis_WithPassword_NoWarning()
    {
        var cfg = Config(new()
        {
            ["Cache:Provider"] = "redis",
            ["Cache:Redis:ConnectionString"] = "localhost:6379,password=s3cret"
        });
        Assert.Empty(ConfigurationValidator.CollectWarnings(cfg));
    }

    [Fact]
    public void MemoryProvider_NoWarning()
    {
        var cfg = Config(new() { ["Cache:Provider"] = "memory" });
        Assert.Empty(ConfigurationValidator.CollectWarnings(cfg));
    }
}
