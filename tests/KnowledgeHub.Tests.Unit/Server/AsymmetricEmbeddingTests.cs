using KnowledgeHub.Server.Embeddings;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// Unit tests for <see cref="AsymmetricEmbeddingProvider"/> — SPEC-20260924-asymmetric-embeddings.
/// </summary>
public sealed class AsymmetricEmbeddingTests
{
    private sealed class RecordingProvider : IEmbeddingProvider
    {
        public List<string> Seen { get; } = [];
        public string ModelId => "fake:test";
        public int Dimensions => 3;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            Seen.Add(text);
            return Task.FromResult(new float[] { 1, 0, 0 });
        }
    }

    private static AsymmetricEmbeddingProvider Sut(
        RecordingProvider inner, EmbeddingOptions? options = null) =>
        new(inner, options ?? new EmbeddingOptions
        {
            Model = "nomic-embed-text",
            Asymmetric = new EmbeddingOptions.AsymmetricOptions { Enabled = true }
        });

    [Fact]
    public async Task EmbedQueryAsync_NomicModel_AppliesSearchQueryPrefix()
    {
        var inner = new RecordingProvider();
        var sut = Sut(inner);

        await sut.EmbedQueryAsync("what is rag");

        Assert.Equal("search_query: what is rag", inner.Seen.Single());
    }

    [Fact]
    public async Task EmbedDocumentAsync_NomicModel_AppliesSearchDocumentPrefix()
    {
        var inner = new RecordingProvider();
        var sut = Sut(inner);

        await sut.EmbedDocumentAsync("chunk text");

        Assert.Equal("search_document: chunk text", inner.Seen.Single());
    }

    [Fact]
    public async Task EmbedQueryAsync_AlreadyPrefixed_DoesNotDoublePrefix()
    {
        var inner = new RecordingProvider();
        var sut = Sut(inner);

        await sut.EmbedQueryAsync("search_query: already");

        Assert.Equal("search_query: already", inner.Seen.Single());
    }

    [Fact]
    public async Task EmbedQueryAsync_ExplicitPrefix_WinsOverAuto()
    {
        var inner = new RecordingProvider();
        var sut = Sut(inner, new EmbeddingOptions
        {
            Model = "nomic-embed-text",
            Asymmetric = new EmbeddingOptions.AsymmetricOptions
            {
                Enabled = true,
                QueryPrefix = "Q> "
            }
        });

        await sut.EmbedQueryAsync("text");

        Assert.Equal("Q> text", inner.Seen.Single());
    }

    [Fact]
    public async Task EmbedAsync_Symmetric_PassesTextThrough()
    {
        var inner = new RecordingProvider();
        var sut = Sut(inner);

        await sut.EmbedAsync("raw text");

        Assert.Equal("raw text", inner.Seen.Single());
    }

    [Fact]
    public async Task EmbedDocumentBatchAsync_AppliesDocumentPrefixToAll()
    {
        var inner = new RecordingProvider();
        var sut = Sut(inner);

        await sut.EmbedDocumentBatchAsync(["a", "b"]);

        Assert.Equal(new[] { "search_document: a", "search_document: b" }, inner.Seen);
    }

    [Fact]
    public void ModelId_CarriesAsymMarker()
    {
        var sut = Sut(new RecordingProvider());
        Assert.Equal("fake:test+asym", sut.ModelId);
    }
}
