using KnowledgeHub.Server.Embeddings;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-02 RF-004 support: deterministic embeddings + codec round-trip + cosine.
public class EmbeddingsTests
{
    [Fact]
    public async Task DeterministicProvider_SameInput_SameVector()
    {
        var provider = new DeterministicEmbeddingProvider();
        var a = await provider.EmbedAsync("arquitetura de software em dotnet");
        var b = await provider.EmbedAsync("arquitetura de software em dotnet");
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task DeterministicProvider_OutputIsNormalized_AndCorrectDimensions()
    {
        var provider = new DeterministicEmbeddingProvider(384);
        var v = await provider.EmbedAsync("hello world");
        Assert.Equal(384, v.Length);
        var norm = Math.Sqrt(v.Sum(x => x * (double)x));
        Assert.Equal(1.0, norm, precision: 4);
    }

    [Fact]
    public async Task DeterministicProvider_SimilarTexts_ScoreHigherThanUnrelated()
    {
        var provider = new DeterministicEmbeddingProvider();
        var query = await provider.EmbedAsync("vector search embeddings");
        var related = await provider.EmbedAsync("vector search over embedding chunks");
        var unrelated = await provider.EmbedAsync("receita de bolo de chocolate");

        var relatedScore = EmbeddingVectorCodec.CosineSimilarity(query, related);
        var unrelatedScore = EmbeddingVectorCodec.CosineSimilarity(query, unrelated);
        Assert.True(relatedScore > unrelatedScore, $"related {relatedScore} should beat unrelated {unrelatedScore}");
    }

    [Fact]
    public void Codec_RoundTrip_PreservesValues()
    {
        var original = new float[] { 0.5f, -1.25f, 3.14f, 0f };
        var bytes = EmbeddingVectorCodec.ToBytes(original);
        var back = EmbeddingVectorCodec.FromBytes(bytes);
        Assert.Equal(original, back);
    }

    [Fact]
    public void Cosine_IdenticalVectors_IsOne_Orthogonal_IsZero()
    {
        var a = new float[] { 1, 0, 0 };
        var b = new float[] { 0, 1, 0 };
        Assert.Equal(1.0, EmbeddingVectorCodec.CosineSimilarity(a, a), precision: 5);
        Assert.Equal(0.0, EmbeddingVectorCodec.CosineSimilarity(a, b), precision: 5);
    }

    [Fact]
    public void Redact_MasksSensitiveKeys()
    {
        var redacted = KnowledgeHub.Server.Services.KnowledgeSourceService.Redact(
            """{"connectionString":"Server=x;Password=p","path":"/v"}""");
        Assert.Equal("***", redacted!["connectionString"]!.GetValue<string>());
        Assert.Equal("/v", redacted["path"]!.GetValue<string>());
    }
}
