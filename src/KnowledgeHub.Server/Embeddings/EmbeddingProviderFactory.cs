using Microsoft.Extensions.Options;

namespace KnowledgeHub.Server.Embeddings;

/// <summary>Selects the concrete <see cref="IEmbeddingProvider"/> from configuration.</summary>
public static class EmbeddingProviderFactory
{
    public static IEmbeddingProvider Create(EmbeddingOptions options, IHttpClientFactory httpClientFactory)
    {
        var http = httpClientFactory.CreateClient("embeddings");
        return options.Provider.ToLowerInvariant() switch
        {
            "ollama" => new OllamaEmbeddingProvider(http, options),
            "openai" => new OpenAiEmbeddingProvider(http, options),
            // SPEC-20260917-onnx-local-embeddings: local all-MiniLM-L6-v2 —
            // missing artifacts fail fast with download instructions.
            "onnx" => OnnxEmbeddingProvider.Load(options.ModelPath),
            _ => new DeterministicEmbeddingProvider(options.Dimensions)
        };
    }
}
