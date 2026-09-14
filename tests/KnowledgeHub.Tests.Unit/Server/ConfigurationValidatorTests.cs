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
}
