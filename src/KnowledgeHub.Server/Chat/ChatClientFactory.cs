using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Chat;

/// <summary>Selects the concrete <see cref="IChatClient"/> from configuration; null when Provider=none.</summary>
public static class ChatClientFactory
{
    public static IChatClient? Create(ChatProviderOptions options, IHttpClientFactory httpClientFactory)
    {
        var http = httpClientFactory.CreateClient("chat");
        return options.Provider.ToLowerInvariant() switch
        {
            "ollama" => new OllamaChatClient(http, options),
            "openai" => new OpenAiChatClient(http, options),
            _ => null
        };
    }
}
