using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Chat;

/// <summary>
/// OpenAI-compatible chat (SPEC-20260914-llm-answer-synthesis RF-001):
/// POST {Endpoint}/v1/chat/completions {model, messages, temperature?, max_tokens?}
/// → choices[0].message.content. Works against OpenAI and compatible gateways.
/// </summary>
public sealed class OpenAiChatClient : HttpChatClient
{
    private readonly string? _apiKey;

    public OpenAiChatClient(HttpClient http, ChatProviderOptions options) : base(http, options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Endpoint, "Chat:Endpoint");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model, "Chat:Model");
        http.BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/");
        _apiKey = options.ApiKey;
    }

    protected override async Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var temperature = (double?)(options?.Temperature) ?? Options.Temperature;
        var maxTokens = options?.MaxOutputTokens ?? Options.MaxTokens;

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(new OpenAiChatRequest(
                Options.Model!,
                Map(messages).Select(m => new OpenAiChatMessage(m.Role, m.Content)).ToArray(),
                temperature,
                maxTokens))
        };
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ChatProviderException($"OpenAI chat failed with HTTP {(int)response.StatusCode}");

        var payload = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken);
        var text = payload?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new ChatProviderException("OpenAI returned an empty chat response");

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = Options.Model };
    }

    private sealed record OpenAiChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] OpenAiChatMessage[] Messages,
        [property: JsonPropertyName("temperature")] double? Temperature,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens);

    private sealed record OpenAiChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OpenAiChatChoice(
        [property: JsonPropertyName("message")] OpenAiChatMessage? Message);

    private sealed record OpenAiChatResponse(
        [property: JsonPropertyName("choices")] OpenAiChatChoice[]? Choices);
}
