using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Chat;

/// <summary>
/// Ollama chat (SPEC-20260914-llm-answer-synthesis RF-001):
/// POST {Endpoint}/api/chat {model, messages, stream:false, options}
/// → message.content. Errors are sanitized — never echo endpoint internals beyond status.
/// </summary>
public sealed class OllamaChatClient : HttpChatClient
{
    public OllamaChatClient(HttpClient http, ChatProviderOptions options) : base(http, options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint, "Chat:Endpoint");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model, "Chat:Model");
        http.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
    }

    protected override async Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var sampling = new Dictionary<string, object>();
        var temperature = (double?)(options?.Temperature) ?? Options.Temperature;
        var maxTokens = options?.MaxOutputTokens ?? Options.MaxTokens;
        if (temperature is not null)
            sampling["temperature"] = temperature.Value;
        if (maxTokens is not null)
            sampling["num_predict"] = maxTokens.Value;

        var request = new OllamaChatRequest(
            Options.Model!,
            Map(messages).Select(m => new OllamaChatMessage(m.Role, m.Content)).ToArray(),
            sampling.Count == 0 ? null : sampling);

        var response = await Http.PostAsJsonAsync("api/chat", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ChatProviderException($"Ollama chat failed with HTTP {(int)response.StatusCode}");

        var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
        var text = payload?.Message?.Content
            ?? throw new ChatProviderException("Ollama returned an empty chat response");

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = Options.Model };
    }

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] OllamaChatMessage[] Messages,
        [property: JsonPropertyName("options")] Dictionary<string, object>? Sampling)
    {
        [JsonPropertyName("stream")]
        public bool Stream => false;
    }

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaChatMessage? Message);
}
