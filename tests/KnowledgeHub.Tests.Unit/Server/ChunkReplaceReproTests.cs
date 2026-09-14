using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// Repro for DbUpdateConcurrencyException on DocumentFile re-sync:
/// load doc Include(Chunks) → RemoveRange chunks → assign new chunk list → SaveChanges.
/// </summary>
public sealed class ChunkReplaceReproTests
{
    private static (SqliteConnection, DbContextOptions<KnowledgeHubDbContext>) NewDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<KnowledgeHubDbContext>()
            .UseSqlite(conn)
            .Options;
        using var ctx = new KnowledgeHubDbContext(opts);
        ctx.Database.EnsureCreated();
        return (conn, opts);
    }

    private static KnowledgeSource SeedSource(KnowledgeHubDbContext db)
    {
        var src = new KnowledgeSource { Name = "s", SourceType = SourceType.DocumentFile };
        db.Sources.Add(src);
        db.SaveChanges();
        return src;
    }

    [Fact]
    public async Task ReplaceChunks_SameContext_Works()
    {
        var (conn, opts) = NewDb();
        await using var _ = conn;
        Guid docId;
        await using (var db = new KnowledgeHubDbContext(opts))
        {
            var src = SeedSource(db);
            var doc = new KnowledgeDocument
            {
                KnowledgeSourceId = src.Id,
                Title = "t",
                UriReference = "f.txt",
                ContentHash = "v1",
                Chunks = [new DocumentChunk { ChunkIndex = 0, TextContent = "version one" }]
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();
            docId = doc.Id;
        }

        await using (var db = new KnowledgeHubDbContext(opts))
        {
            var doc = await db.Documents.Include(d => d.Chunks).FirstAsync(d => d.Id == docId);
            db.Chunks.RemoveRange(doc.Chunks);
            doc.ContentHash = "v2";
            var newChunks = new List<DocumentChunk>
            {
                new() { KnowledgeDocumentId = doc.Id, ChunkIndex = 0, TextContent = "version two" }
            };
            db.Chunks.AddRange(newChunks);
            await db.SaveChangesAsync();

            // now embed-path upsert on the same context
            var chunk = await db.Chunks.FindAsync([newChunks[0].Id]);
            Assert.NotNull(chunk);
            chunk!.Embedding = [1, 2, 3];
            chunk.EmbeddingModel = "m";
            await db.SaveChangesAsync();
        }
    }
}
