using System.Net;
using System.Text;
using KnowledgeHub.Server.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

// Regression: gateways OpenAI-compatible restritos (ex.: OmniRoute) rejeitam
// campos opcionais serializados como null — "temperature: must be a number"
// (HTTP 400). O request deve omitir temperature/max_tokens/tools quando null.
public sealed class OpenAiChatClientTests
{
    /// <summary>Handler fake que captura o corpo da requisição e responde um completion mínimo.</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        /// <summary>Corpo JSON da última requisição capturada.</summary>
        public string? LastBody { get; private set; }

        /// <summary>Lê o corpo do request e devolve uma resposta OpenAI mínima.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>Monta um OpenAiChatClient apontando para o handler de captura.</summary>
    private static (OpenAiChatClient Client, CaptureHandler Handler) Sut(ChatProviderOptions? options = null)
    {
        var handler = new CaptureHandler();
        var http = new HttpClient(handler);
        var client = new OpenAiChatClient(http, options ?? new ChatProviderOptions
        {
            Provider = "openai",
            Endpoint = "https://gw.test",
            Model = "m"
        });
        return (client, handler);
    }

    /// <summary>Sem temperature/max_tokens configurados, os campos não aparecem no JSON.</summary>
    [Fact]
    public async Task Request_OmitsNullOptionalFields()
    {
        var (client, handler) = Sut();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "oi")]);

        Assert.NotNull(handler.LastBody);
        Assert.DoesNotContain("\"temperature\"", handler.LastBody!);
        Assert.DoesNotContain("\"max_tokens\"", handler.LastBody!);
        Assert.DoesNotContain("\"tools\"", handler.LastBody!);
    }

    /// <summary>Com temperature/max_tokens configurados, os campos aparecem com valores numéricos.</summary>
    [Fact]
    public async Task Request_IncludesConfiguredFields()
    {
        var (client, handler) = Sut(new ChatProviderOptions
        {
            Provider = "openai",
            Endpoint = "https://gw.test",
            Model = "m",
            Temperature = 0.5,
            MaxTokens = 128
        });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "oi")]);

        Assert.Contains("\"temperature\":0.5", handler.LastBody);
        Assert.Contains("\"max_tokens\":128", handler.LastBody);
    }

    /// <summary>Mensagens comuns não carregam tool_calls/tool_call_id nulos.</summary>
    [Fact]
    public async Task Request_OmitsNullMessageToolFields()
    {
        var (client, handler) = Sut();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "oi")]);

        Assert.DoesNotContain("\"tool_calls\"", handler.LastBody!);
        Assert.DoesNotContain("\"tool_call_id\"", handler.LastBody!);
    }
}
