using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Graph;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeHub.Tests.Unit.Graph;

/// <summary>
/// SPEC-20260923-graphrag: entity resolution (RF-003), provenance-bound edges
/// (RF-002), bounded traversal/path/impact (RF-004), cascade delete (AC).
/// </summary>
public sealed class GraphTests
{
    private static async Task<(SqliteConnection, KnowledgeHubDbContext, SqliteKnowledgeGraphStore)> SeedAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var db = new KnowledgeHubDbContext(
            new DbContextOptionsBuilder<KnowledgeHubDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();
        return (conn, db, new SqliteKnowledgeGraphStore(db, NullLogger<SqliteKnowledgeGraphStore>.Instance));
    }

    private static KgEdge Edge(KgNode from, KgNode to, string kind, KnowledgeDocument doc, Guid sourceId) =>
        new()
        {
            FromNodeId = from.Id,
            ToNodeId = to.Id,
            Kind = kind,
            EvidenceChunkId = Guid.NewGuid(),
            KnowledgeDocumentId = doc.Id,
            KnowledgeSourceId = sourceId
        };

    // ---- EntityResolver ----------------------------------------------------

    [Fact]
    public void Normalize_CollapsesCaseAndPunctuation()
    {
        Assert.Equal("db y", EntityResolver.Normalize("  DB-Y "));
        Assert.Equal("db y", EntityResolver.Normalize("db_y"));
        Assert.Equal("db y", EntityResolver.Normalize("DB  y"));
        Assert.Equal("api x", EntityResolver.Normalize("API-X"));
    }

    [Fact]
    public void NormalizeKind_FallsBackToMentions()
    {
        Assert.Equal("DEPENDS_ON", EntityResolver.NormalizeKind("depends_on"));
        Assert.Equal("MENTIONS", EntityResolver.NormalizeKind("WEIRD_KIND"));
        Assert.Equal("MENTIONS", EntityResolver.NormalizeKind(null));
    }

    // ---- Extractor parse ----------------------------------------------------

    [Fact]
    public void Parse_ValidJson_ReturnsEntitiesAndRelations()
    {
        var result = EntityExtractor.Parse("""
            {"entities":[{"name":"API-X","type":"api"},{"name":"DB-Y","type":"database"}],
             "relations":[{"from":"API-X","to":"DB-Y","kind":"DEPENDS_ON","evidence":1}]}
            """);
        Assert.NotNull(result);
        Assert.Equal(2, result.Entities.Count);
        Assert.Single(result.Relations);
        Assert.Equal(1, result.Relations[0].EvidenceIndex);
    }

    [Fact]
    public void Parse_ProseAroundJson_StillParses()
    {
        var result = EntityExtractor.Parse(
            "Here is the extraction:\n```json\n{\"entities\":[{\"name\":\"A\"}],\"relations\":[]}\n```\nDone.");
        Assert.NotNull(result);
        Assert.Single(result.Entities);
    }

    [Fact]
    public void Parse_Malformed_ReturnsNull()
    {
        Assert.Null(EntityExtractor.Parse("not json at all"));
        Assert.Null(EntityExtractor.Parse("{ broken "));
        Assert.Null(EntityExtractor.Parse(null));
    }

    // ---- ResolveNode: merge + conflict --------------------------------------

    [Fact]
    public async Task ResolveNode_VariantSpelling_MergesWithAlias()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var sourceId = Guid.NewGuid();

        var a = await store.ResolveNodeAsync("DB-Y", "database", sourceId, default);
        var b = await store.ResolveNodeAsync("db y", "database", sourceId, default);

        Assert.Equal(a.Id, b.Id);
        var alias = await db.KgAliases.SingleAsync();
        Assert.Equal("merge", alias.Reason);
        Assert.Equal(a.Id, alias.KgNodeId);
    }

    [Fact]
    public async Task ResolveNode_SameNameDifferentType_DistinctNodesWithConflictAliases()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var sourceId = Guid.NewGuid();

        var svc = await store.ResolveNodeAsync("Atlas", "service", sourceId, default);
        var srv = await store.ResolveNodeAsync("atlas", "database", sourceId, default);

        Assert.NotEqual(svc.Id, srv.Id);
        var conflicts = await db.KgAliases.Where(a => a.Reason == "conflict").ToListAsync();
        Assert.Equal(2, conflicts.Count); // both sides recorded
    }

    [Fact]
    public async Task FindNode_ResolvesThroughAlias()
    {
        var (conn, _, store) = await SeedAsync();
        await using var _ = conn;
        var node = await store.ResolveNodeAsync("DB-Y", "database", Guid.NewGuid(), default);
        var found = await store.FindNodeAsync("db y", default);
        Assert.Equal(node.Id, found!.Id);
    }

    // ---- Edges: provenance + dedup ------------------------------------------

    [Fact]
    public async Task AddEdges_DeduplicatesAndRequiresEvidence()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var (doc, a, b) = await SeedDocAndNodes(db, store);

        var edge = Edge(a, b, "DEPENDS_ON", doc, doc.KnowledgeSourceId);
        var first = await store.AddEdgesAsync([edge], default);
        var dup = Edge(a, b, "DEPENDS_ON", doc, doc.KnowledgeSourceId);
        dup.EvidenceChunkId = edge.EvidenceChunkId; // same evidence → dedup
        var second = await store.AddEdgesAsync([dup], default);
        var noEvidence = Edge(a, b, "USES", doc, doc.KnowledgeSourceId);
        noEvidence.EvidenceChunkId = Guid.Empty;
        var third = await store.AddEdgesAsync([noEvidence], default);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(0, third);
        Assert.Equal(1, await db.KgEdges.CountAsync());
    }

    // ---- Traversal ----------------------------------------------------------

    [Fact]
    public async Task Traverse_BoundedDepth_AndCycleSafe()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var (doc, a, b) = await SeedDocAndNodes(db, store);
        var c = await store.ResolveNodeAsync("C", "service", doc.KnowledgeSourceId, default);

        // A→B→C→A cycle: every node visited once.
        await store.AddEdgesAsync([
            Edge(a, b, "USES", doc, doc.KnowledgeSourceId),
            Edge(b, c, "USES", doc, doc.KnowledgeSourceId),
            Edge(c, a, "USES", doc, doc.KnowledgeSourceId)], default);

        var depth1 = await store.TraverseAsync(a.Id, GraphDirection.Outbound, 1, 200, default);
        Assert.Single(depth1.Edges);

        var depth3 = await store.TraverseAsync(a.Id, GraphDirection.Outbound, 3, 200, default);
        Assert.Equal(3, depth3.Edges.Count); // cycle does not loop forever
        Assert.Equal(3, depth3.Nodes.DistinctBy(n => n.Id).Count());
        Assert.False(depth3.Truncated);
    }

    [Fact]
    public async Task Traverse_MaxEdges_SetsTruncated()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var (doc, a, _) = await SeedDocAndNodes(db, store);
        var edges = new List<KgEdge>();
        for (var i = 0; i < 5; i++)
        {
            var n = await store.ResolveNodeAsync($"n{i}", "service", doc.KnowledgeSourceId, default);
            edges.Add(Edge(a, n, "USES", doc, doc.KnowledgeSourceId));
        }
        await store.AddEdgesAsync(edges, default);

        var sub = await store.TraverseAsync(a.Id, GraphDirection.Outbound, 1, 2, default);
        Assert.Equal(2, sub.Edges.Count);
        Assert.True(sub.Truncated);
    }

    [Fact]
    public async Task FindPaths_ReturnsShortestOutboundPath()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var (doc, a, b) = await SeedDocAndNodes(db, store);
        var c = await store.ResolveNodeAsync("C", "service", doc.KnowledgeSourceId, default);
        await store.AddEdgesAsync([
            Edge(a, b, "USES", doc, doc.KnowledgeSourceId),
            Edge(b, c, "USES", doc, doc.KnowledgeSourceId)], default);

        var paths = await store.FindPathsAsync(a.Id, c.Id, 3, 5, default);
        Assert.Single(paths);
        Assert.Equal(2, paths[0].Count); // a→b→c

        Assert.Empty(await store.FindPathsAsync(c.Id, a.Id, 3, 5, default)); // direction matters
    }

    [Fact]
    public async Task Impact_ReturnsOneHopDependentsAndDocuments()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var (doc, a, b) = await SeedDocAndNodes(db, store);
        var c = await store.ResolveNodeAsync("C", "service", doc.KnowledgeSourceId, default);
        await store.AddEdgesAsync([
            Edge(a, b, "DEPENDS_ON", doc, doc.KnowledgeSourceId),
            Edge(c, b, "AFFECTED_BY", doc, doc.KnowledgeSourceId)], default);

        var sub = await store.ImpactAsync(b.Id, 200, default);
        Assert.Equal(2, sub.Edges.Count);
        Assert.Contains(sub.Nodes, n => n.Id == a.Id);
        Assert.Contains(sub.Nodes, n => n.Id == c.Id);
        Assert.All(sub.Edges, e => Assert.Equal(doc.Title, e.Document.Title));
    }

    // ---- Cascade -------------------------------------------------------------

    [Fact]
    public async Task DeletingDocument_CascadesItsEdges()
    {
        var (conn, db, store) = await SeedAsync();
        await using var _ = conn;
        var (doc, a, b) = await SeedDocAndNodes(db, store);
        await store.AddEdgesAsync([Edge(a, b, "USES", doc, doc.KnowledgeSourceId)], default);
        Assert.Equal(1, await db.KgEdges.CountAsync());

        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.KgEdges.CountAsync());
    }

    private static async Task<(KnowledgeDocument Doc, KgNode A, KgNode B)> SeedDocAndNodes(
        KnowledgeHubDbContext db, SqliteKnowledgeGraphStore store)
    {
        var source = new KnowledgeSource
        {
            Name = "src",
            SourceType = KnowledgeHub.Shared.Contracts.SourceType.DocumentFile,
            IsActive = true
        };
        var doc = new KnowledgeDocument
        {
            Title = "doc",
            UriReference = "doc.md",
            KnowledgeSourceId = source.Id
        };
        db.Sources.Add(source);
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var a = await store.ResolveNodeAsync("A", "service", source.Id, default);
        var b = await store.ResolveNodeAsync("B", "database", source.Id, default);
        return (doc, a, b);
    }
}
