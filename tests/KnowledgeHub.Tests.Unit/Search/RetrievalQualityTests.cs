using KnowledgeHub.Server.Search;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Tests.Unit.Search;

/// <summary>
/// SPEC-20260923-retrieval-quality: filter resolution/validation, rerank
/// score parsing, legacy contract deserialization.
/// </summary>
public class RetrievalQualityTests
{
    // ---- RF-003: filter validation -----------------------------------------

    [Fact]
    public void Filter_Null_ResolvesEmpty()
    {
        Assert.True(ResolvedSearchFilter.TryResolve(null, out var f, out var err));
        Assert.Null(err);
        Assert.True(f.IsEmpty);
    }

    [Fact]
    public void Filter_EmptyObject_ResolvesEmpty()
    {
        Assert.True(ResolvedSearchFilter.TryResolve(new SearchFilter(), out var f, out _));
        Assert.True(f.IsEmpty);
    }

    [Fact]
    public void Filter_ValidSourceType_Resolves()
    {
        Assert.True(ResolvedSearchFilter.TryResolve(
            new SearchFilter { SourceType = "obsidianvault" }, out var f, out _));
        Assert.Equal(SourceType.ObsidianVault, f.SourceType);
        Assert.False(f.IsEmpty);
    }

    [Fact]
    public void Filter_InvalidSourceType_Fails()
    {
        Assert.False(ResolvedSearchFilter.TryResolve(
            new SearchFilter { SourceType = "bogus" }, out _, out var err));
        Assert.Contains("bogus", err);
    }

    [Fact]
    public void Filter_InvalidDate_Fails()
    {
        Assert.False(ResolvedSearchFilter.TryResolve(
            new SearchFilter { IndexedAfter = "not-a-date" }, out _, out var err));
        Assert.Contains("indexedAfter", err);
    }

    [Fact]
    public void Filter_Fingerprint_Discriminates()
    {
        ResolvedSearchFilter.TryResolve(new SearchFilter { PathPrefix = "docs/" }, out var a, out _);
        ResolvedSearchFilter.TryResolve(new SearchFilter { PathPrefix = "src/" }, out var b, out _);
        Assert.NotEqual(a.Fingerprint(), b.Fingerprint());
        Assert.Equal("-", new ResolvedSearchFilter(null, null, null, null).Fingerprint());
    }

    // ---- RF-002: rerank score parsing ---------------------------------------

    [Fact]
    public async Task LlmReranker_ParsesScores_AndIgnoresJunk()
    {
        var items = new List<SearchResultItem>
        {
            Item(0), Item(1), Item(2)
        };
        var reranker = new LlmReranker(
            new StubChat("1: 9\n2: 3\n99: 5\nbogus line\n3: 7.5"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LlmReranker>.Instance);

        var scores = await reranker.RerankAsync("q", items);

        Assert.Equal(3, scores.Count);
        Assert.Equal(9, scores.Single(s => s.ChunkId == items[0].ChunkId).Score);
        Assert.Equal(3, scores.Single(s => s.ChunkId == items[1].ChunkId).Score);
        Assert.Equal(7.5, scores.Single(s => s.ChunkId == items[2].ChunkId).Score);
    }

    [Fact]
    public async Task NoOpReranker_ReturnsEmpty()
    {
        Assert.Empty(await NoOpReranker.Instance.RerankAsync("q", [Item(0)]));
    }

    // ---- RF-004: contract backward-compat ------------------------------------

    [Fact]
    public void LegacyPayload_Deserializes_WithoutNewFields()
    {
        // A v1 cached payload has none of the new fields.
        const string legacy = """
            {"chunkText":"t","documentTitle":"d","sourceName":"s",
             "sourceId":"00000000-0000-0000-0000-000000000001",
             "score":0.5,"uriReference":"u","sourceType":"ObsidianVault"}
            """;
        var item = System.Text.Json.JsonSerializer.Deserialize<SearchResultItem>(
            legacy, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(item);
        Assert.Null(item.ChunkId);
        Assert.Null(item.DocumentId);
        Assert.Null(item.Metadata);
        Assert.Null(item.IndexedAt);
        Assert.Null(item.SuspicionFlags);
    }

    private static SearchResultItem Item(int seed) => new()
    {
        ChunkText = $"text {seed}",
        DocumentTitle = "d",
        SourceName = "s",
        SourceId = Guid.Empty,
        Score = 1.0 - seed * 0.1,
        UriReference = $"u{seed}",
        ChunkId = Guid.NewGuid()
    };

    private sealed class StubChat(string reply) : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, reply)));

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
