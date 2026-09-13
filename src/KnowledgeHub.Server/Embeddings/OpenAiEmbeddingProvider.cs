using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace KnowledgeHub.Server.Embeddings;

/// <summary>
/// OpenAI-compatible embeddings (SPEC-03 RF-003): POST {Endpoint}/v1/embeddings
/// {model, input} + Bearer {ApiKey}. Covers OpenAI, Azure-compatible gateways,
/// LM Studio, vLLM, etc. The ApiKey is never logged or included in errors.
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _dimensions;

    public OpenAiEmbeddingProvider(HttpClient http, EmbeddingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint, "Embeddings:Endpoint");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model, "Embeddings:Model");
        _http = http;
        _http.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        _model = options.Model;
        _dimensions = options.Dimensions;
    }

    public string ModelId => $"openai:{_model}";
    public int Dimensions => _dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("v1/embeddings",
            new OpenAiRequest(_model, text), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new EmbeddingProviderException($"OpenAI embeddings failed with HTTP {(int)response.StatusCode}");

        var payload = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken);
        var vector = payload?.Data?.FirstOrDefault()?.Embedding
            ?? throw new EmbeddingProviderException("OpenAI returned no embedding");
        return FitDimensions(vector);
    }

    private float[] FitDimensions(float[] vector)
    {
        if (vector.Length == _dimensions)
            return vector;
        var fitted = new float[_dimensions];
        Array.Copy(vector, fitted, Math.Min(vector.Length, _dimensions));
        return fitted;
    }

    private sealed record OpenAiRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private sealed record OpenAiResponse(
        [property: JsonPropertyName("data")] List<OpenAiEmbedding>? Data);

    private sealed record OpenAiEmbedding(
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
