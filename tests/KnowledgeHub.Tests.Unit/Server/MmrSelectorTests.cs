using KnowledgeHub.Server.Search;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// Unit tests for <see cref="MmrSelector"/> — SPEC-20260924-mmr-diversity.
/// </summary>
public sealed class MmrSelectorTests
{
    private static MmrSelector.Candidate Cand(
        string name, Guid docId, double score, float[]? vector = null) =>
        new(StableId(name), docId, score, vector);

    private static Guid StableId(string name) =>
        new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(name)).Take(16).ToArray());

    // -------------------------------------------------------------------------
    // Score floor
    // -------------------------------------------------------------------------

    [Fact]
    public void Select_MinScore_FiltersBelowThreshold()
    {
        // Given: scores 0.9, 0.4, 0.1 and floor 0.3
        var doc = Guid.NewGuid();
        var candidates = new[]
        {
            Cand("a", doc, 0.9), Cand("b", doc, 0.4), Cand("c", doc, 0.1)
        };

        // When
        var result = MmrSelector.Select(candidates, topK: 5, lambda: 0.7, minScore: 0.3);

        // Then
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(StableId("c"), result);
    }

    // -------------------------------------------------------------------------
    // Per-document quota
    // -------------------------------------------------------------------------

    [Fact]
    public void Select_MaxPerDocument_CapsRepeats()
    {
        // Given: 4 candidates from doc A + 2 from doc B
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var candidates = new[]
        {
            Cand("a1", docA, 0.9), Cand("a2", docA, 0.8), Cand("a3", docA, 0.7),
            Cand("a4", docA, 0.6), Cand("b1", docB, 0.55), Cand("b2", docB, 0.5)
        };

        // When
        var result = MmrSelector.Select(candidates, topK: 6, lambda: 1.0, maxPerDocument: 2);

        // Then: at most 2 from docA
        var fromA = result.Count(id => candidates.First(c => c.ChunkId == id).DocumentId == docA);
        Assert.Equal(2, fromA);
        Assert.Equal(4, result.Count); // 2 from A + 2 from B
    }

    // -------------------------------------------------------------------------
    // MMR penalises near-duplicates
    // -------------------------------------------------------------------------

    [Fact]
    public void Select_SimilarVectors_PenalisesDuplicate()
    {
        // Given: a1 and a2 are near-identical vectors; b is orthogonal
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var va = new float[] { 1, 0, 0 };
        var va2 = new float[] { 0.999f, 0.001f, 0 }; // ~identical to va
        var vb = new float[] { 0, 1, 0 };
        var candidates = new[]
        {
            Cand("a1", docA, 0.9, va),
            Cand("a2", docA, 0.89, va2),   // near-dup of a1
            Cand("b1", docB, 0.85, vb)     // less relevant but diverse
        };

        // When: lambda 0.5 gives diversity real weight
        var result = MmrSelector.Select(candidates, topK: 3, lambda: 0.5);

        // Then: a1 first, b1 second (a2 penalised as redundant)
        Assert.Equal(StableId("a1"), result[0]);
        Assert.Equal(StableId("b1"), result[1]);
        Assert.Equal(StableId("a2"), result[2]);
    }

    [Fact]
    public void Select_LambdaOne_IgnoresDiversity()
    {
        // Given same setup but λ=1 → pure relevance order
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var candidates = new[]
        {
            Cand("a1", docA, 0.9, new float[] { 1, 0 }),
            Cand("a2", docA, 0.89, new float[] { 1, 0 }),
            Cand("b1", docB, 0.85, new float[] { 0, 1 })
        };

        // When
        var result = MmrSelector.Select(candidates, topK: 3, lambda: 1.0);

        // Then: relevance order preserved
        Assert.Equal(new[] { StableId("a1"), StableId("a2"), StableId("b1") }, result);
    }

    // -------------------------------------------------------------------------
    // Edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void Select_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(MmrSelector.Select([], topK: 5, lambda: 0.7));
    }

    [Fact]
    public void Select_NullVectors_StillOrdersByRelevance()
    {
        // Given: no vectors — degrades to score order + quota
        var doc = Guid.NewGuid();
        var candidates = new[] { Cand("a", doc, 0.9), Cand("b", doc, 0.5) };

        // When
        var result = MmrSelector.Select(candidates, topK: 5, lambda: 0.3);

        // Then
        Assert.Equal(new[] { StableId("a"), StableId("b") }, result);
    }

    [Fact]
    public void Select_QuotaExhaustsAll_StopsEarly()
    {
        // Given: 3 candidates same doc, quota 1
        var doc = Guid.NewGuid();
        var candidates = new[]
        {
            Cand("a", doc, 0.9), Cand("b", doc, 0.8), Cand("c", doc, 0.7)
        };

        // When
        var result = MmrSelector.Select(candidates, topK: 3, lambda: 0.7, maxPerDocument: 1);

        // Then
        Assert.Single(result);
        Assert.Equal(StableId("a"), result[0]);
    }
}
