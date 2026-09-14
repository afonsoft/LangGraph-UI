namespace KnowledgeHub.Server.Search;

/// <summary>One fused ranking entry: chunk id + per-ranker provenance + RRF score.</summary>
public sealed record FusedHit(Guid ChunkId, int? VectorRank, int? LexicalRank, double Fused);

/// <summary>
/// Reciprocal Rank Fusion (SPEC-20260914-hybrid-retrieval RF-002):
/// score = Σ 1/(k + rank) with k=60 over the vector and lexical rankings.
/// </summary>
public static class RrfFuser
{
    public const int K = 60;

    /// <param name="vectorRanked">Chunk ids ordered best-first by the vector ranker.</param>
    /// <param name="lexicalRanked">Chunk ids ordered best-first by the lexical ranker.</param>
    public static IReadOnlyList<FusedHit> Fuse(
        IReadOnlyList<Guid> vectorRanked,
        IReadOnlyList<Guid> lexicalRanked,
        int topK)
    {
        var scores = new Dictionary<Guid, FusedHit>();

        Accumulate(vectorRanked, isVector: true);
        Accumulate(lexicalRanked, isVector: false);

        return scores.Values
            .OrderByDescending(h => h.Fused)
            .ThenBy(h => h.ChunkId)
            .Take(topK)
            .ToList();

        void Accumulate(IReadOnlyList<Guid> ranked, bool isVector)
        {
            for (var i = 0; i < ranked.Count; i++)
            {
                var rank = i + 1;
                var contribution = 1.0 / (K + rank);
                if (scores.TryGetValue(ranked[i], out var hit))
                    scores[ranked[i]] = hit with
                    {
                        VectorRank = isVector ? rank : hit.VectorRank,
                        LexicalRank = isVector ? hit.LexicalRank : rank,
                        Fused = hit.Fused + contribution
                    };
                else
                    scores[ranked[i]] = new FusedHit(
                        ranked[i],
                        isVector ? rank : null,
                        isVector ? null : rank,
                        contribution);
            }
        }
    }
}
