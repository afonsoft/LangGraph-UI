using KnowledgeHub.Server.Search;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260914-hybrid-retrieval RF-002: RRF k=60 fusion semantics.
public class RrfFuserTests
{
    [Fact]
    public void ItemInBothRankings_BeatsItemInOne()
    {
        var both = Guid.NewGuid();
        var onlyVector = Guid.NewGuid();
        var onlyLexical = Guid.NewGuid();

        var fused = RrfFuser.Fuse(
            vectorRanked: [both, onlyVector],
            lexicalRanked: [both, onlyLexical],
            topK: 10);

        Assert.Equal(both, fused[0].ChunkId);
        Assert.Equal(1, fused[0].VectorRank);
        Assert.Equal(1, fused[0].LexicalRank);
        Assert.Equal(2.0 / (RrfFuser.K + 1), fused[0].Fused, precision: 10);
    }

    [Fact]
    public void DisjointRankings_PreserveRelativeOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var fused = RrfFuser.Fuse(vectorRanked: [a], lexicalRanked: [b], topK: 10);

        Assert.Equal(2, fused.Count);
        Assert.All(fused, h => Assert.Equal(1.0 / (RrfFuser.K + 1), h.Fused, precision: 10));
    }

    [Fact]
    public void TopK_TruncatesFusedList()
    {
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        var fused = RrfFuser.Fuse(vectorRanked: ids, lexicalRanked: [], topK: 3);
        Assert.Equal(3, fused.Count);
        Assert.Equal(ids[0], fused[0].ChunkId);
    }

    [Fact]
    public void EmptyRankings_ReturnEmpty()
    {
        Assert.Empty(RrfFuser.Fuse([], [], 10));
    }
}
