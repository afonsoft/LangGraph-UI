using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>SPEC-20260914-llm-answer-synthesis RF-002: prompt, citations, fallbacks.</summary>
public sealed class AnswerServiceTests
{
    private static readonly ChatProviderOptions Options = new() { Provider = "ollama", Model = "m" };

    private static SearchResultItem Hit(string text = "ctx", string title = "Doc", int n = 1) =>
        new()
        {
            ChunkText = text,
            DocumentTitle = title,
            SourceName = "src",
            SourceId = Guid.NewGuid(),
            Score = 0.9,
            UriReference = $"uri-{n}"
        };

    [Fact]
    public async Task EmptyContext_DeclaresNoMatches_WithoutCallingLlm()
    {
        var svc = new AnswerService(new StubChatClient("should not be used"), Options, NullLogger<AnswerService>.Instance);
        var result = await svc.AnswerAsync("q?", []);

        Assert.Contains("no matching content", result.Answer);
        Assert.Empty(result.Citations);
        Assert.True(result.Generated);
        Assert.Null(result.Model);
    }

    [Fact]
    public async Task NoClient_ThrowsChatProviderException()
    {
        var svc = new AnswerService(null, Options, NullLogger<AnswerService>.Instance);
        Assert.False(svc.IsConfigured);
        await Assert.ThrowsAsync<ChatProviderException>(() => svc.AnswerAsync("q?", [Hit()]));
    }

    [Fact]
    public async Task Answer_MapsValidCitationMarkers_ToRealHits()
    {
        var svc = new AnswerService(new StubChatClient("Answer cites [1] and out-of-range [9]."), Options, NullLogger<AnswerService>.Instance);
        var context = new[] { Hit(title: "Alpha", n: 1), Hit(title: "Beta", n: 2) };
        var result = await svc.AnswerAsync("q?", context);

        Assert.True(result.Generated);
        Assert.Equal("stub-model", result.Model);
        var c = Assert.Single(result.Citations);
        Assert.Equal(1, c.Index);
        Assert.Equal("Alpha", c.Title);
        Assert.Equal("uri-1", c.Uri);
    }

    [Fact]
    public async Task Answer_WithoutMarkers_AttachesAllContextAsCitations()
    {
        var svc = new AnswerService(new StubChatClient("plain answer, no markers"), Options, NullLogger<AnswerService>.Instance);
        var result = await svc.AnswerAsync("q?", [Hit(n: 1), Hit(n: 2)]);
        Assert.Equal(2, result.Citations.Count);
    }

    [Fact]
    public async Task EmptyAnswer_ThrowsChatProviderException()
    {
        var svc = new AnswerService(new StubChatClient("   "), Options, NullLogger<AnswerService>.Instance);
        await Assert.ThrowsAsync<ChatProviderException>(() => svc.AnswerAsync("q?", [Hit()]));
    }

    [Fact]
    public void BuildUserPrompt_NumbersContext_AndAppendsQuestion()
    {
        var prompt = AnswerService.BuildUserPrompt("what?", [Hit(text: "body", title: "T", n: 3)]);
        Assert.Contains("[1] T — src (uri-3)", prompt);
        Assert.Contains("body", prompt);
        Assert.EndsWith("Question: what?", prompt);
    }

    [Fact]
    public void ChatClientFactory_NoneAndUnknown_ReturnNull()
    {
        var httpFactory = new StubHttpClientFactory();
        Assert.Null(ChatClientFactory.Create(new ChatProviderOptions { Provider = "none" }, httpFactory));
        Assert.Null(ChatClientFactory.Create(new ChatProviderOptions { Provider = "weird" }, httpFactory));
    }

    [Fact]
    public void ChatClientFactory_Ollama_ReturnsClient()
    {
        var client = ChatClientFactory.Create(
            new ChatProviderOptions { Provider = "ollama", Endpoint = "http://localhost:11434", Model = "llama3" },
            new StubHttpClientFactory());
        Assert.IsType<OllamaChatClient>(client);
    }

    private sealed class StubChatClient(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = "stub-model" });

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
