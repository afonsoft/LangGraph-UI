using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Ingestion.Chunking;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260924-semantic-chunking: breakpoint detection, token ceilings,
/// merge floor and determinism.
/// </summary>
public sealed class SemanticChunkerTests
{
    /// <summary>Embeds by topic keyword: sentences containing "dogs" → v1,
    /// "python" → v2 — orthogonal topics produce distance ~1.</summary>
    private sealed class TopicEmbeddings : IEmbeddingProvider
    {
        public string ModelId => "fake:topics";
        public int Dimensions => 2;
        public int BatchCalls { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(VectorFor(text));

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            BatchCalls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(VectorFor).ToList());
        }

        private static float[] VectorFor(string text) =>
            text.Contains("dogs", StringComparison.OrdinalIgnoreCase)
                ? [1f, 0f]
                : text.Contains("python", StringComparison.OrdinalIgnoreCase)
                    ? [0f, 1f]
                    : [0.7f, 0.7f]; // neutral-ish
    }

    private static SemanticTextChunker Sut(IEmbeddingProvider emb, int minTokens = 5) =>
        new(emb, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingestion:Semantic:MinTokens"] = minTokens.ToString(),
            ["Ingestion:Semantic:BreakpointPercentile"] = "95"
        }).Build());

    [Fact]
    public async Task ChunkAsync_TopicShift_BreaksAtTransition()
    {
        // Given: 3 sentences about dogs, then 3 about python — orthogonal vectors
        var text = "Dogs are loyal animals. Dogs need daily walks. Dogs enjoy playing fetch. " +
                   "Python is a programming language. Python has dynamic typing. Python is widely used.";
        var emb = new TopicEmbeddings();

        // When
        var pieces = await Sut(emb, minTokens: 5).ChunkAsync(text, maxTokens: 500, overlapTokens: 0);

        // Then: two thematic chunks, cut at the topic boundary
        Assert.Equal(2, pieces.Count);
        Assert.Contains("dogs", pieces[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("python", pieces[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("python", pieces[1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChunkAsync_ExceedingMaxTokens_HardCut()
    {
        // Same topic throughout — only the maxTokens ceiling can force a cut
        var text = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"Dogs sentence number {i}."));
        var pieces = await Sut(new TopicEmbeddings(), minTokens: 5)
            .ChunkAsync(text, maxTokens: 30, overlapTokens: 0);

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.True(p.Text.Length / 4 <= 40)); // 30 tokens + a word of slack
    }

    [Fact]
    public async Task ChunkAsync_Deterministic_SameInputSameOutput()
    {
        var text = "Dogs are great. Dogs bark loudly. Python rocks. Python is typed dynamically.";
        var a = await Sut(new TopicEmbeddings(), 3).ChunkAsync(text, 500, 0);
        var b = await Sut(new TopicEmbeddings(), 3).ChunkAsync(text, 500, 0);

        Assert.Equal(a.Select(p => p.Text), b.Select(p => p.Text));
    }

    [Fact]
    public async Task ChunkAsync_SingleSentence_SingleChunk()
    {
        var pieces = await Sut(new TopicEmbeddings()).ChunkAsync("Just one sentence.", 500, 0);
        Assert.Single(pieces);
    }

    [Fact]
    public async Task ChunkAsync_OverCap_ThrowsForFallback()
    {
        var emb = new TopicEmbeddings();
        var sut = new SemanticTextChunker(emb,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Ingestion:Semantic:MaxSentencesPerDoc"] = "2" }).Build());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ChunkAsync("One. Two. Three. Four.", 500, 0));
        Assert.Equal(0, emb.BatchCalls); // never embedded — bail early
    }

    [Fact]
    public void SplitSentences_HandlesTerminatorsAndParagraphs()
    {
        var parts = SemanticTextChunker.SplitSentences(
            "First sentence. Second one!\n\nNew paragraph here?");
        Assert.Equal(3, parts.Count);
    }
}
