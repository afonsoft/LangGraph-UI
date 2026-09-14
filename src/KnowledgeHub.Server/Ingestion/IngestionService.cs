using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Search;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Server.VectorStore;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Ingestion;

/// <summary>
/// Incremental Obsidian vault indexing (SPEC-03 RF-004): scans <c>**/*.md</c> under the
/// configured path, skips unchanged files by SHA-256, re-chunks + re-embeds changed
/// files, removes documents whose files disappeared. One sync per source at a time.
/// </summary>
public sealed class IngestionService(
    IServiceScopeFactory scopeFactory,
    IEmbeddingProvider embeddings,
    IConfiguration configuration,
    ILogger<IngestionService> logger) : IIngestionService
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SourceLocks = new();

    public async Task<SyncResultDto> SyncAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var gate = SourceLocks.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
            return new SyncResultDto { Status = "skipped", Reason = "sync already running", SourceId = sourceId };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            var vectors = scope.ServiceProvider.GetRequiredService<IVectorStore>();

            var source = await db.Sources.FindAsync([sourceId], cancellationToken);
            if (source is null)
                return Fail(sourceId, "source not found");

            if (source.SourceType != SourceType.ObsidianVault)
                return new SyncResultDto { Status = "skipped", Reason = $"connector {source.SourceType} not implemented", SourceId = sourceId, DurationMs = stopwatch.Elapsed.TotalMilliseconds };

            var root = ResolveVaultRoot(source.ConfigurationJson);
            if (root is null || !Directory.Exists(root))
                return Fail(sourceId, $"vault path not found or not configured");

            var files = EnumerateMarkdown(root);
            var existing = await db.Documents
                .Include(d => d.Chunks)
                .Where(d => d.KnowledgeSourceId == sourceId)
                .ToDictionaryAsync(d => d.UriReference, cancellationToken);

            var processed = 0; var skipped = 0; var removed = 0; var chunksCreated = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, file);
                seen.Add(relative);

                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));

                if (existing.TryGetValue(relative, out var doc) && doc.ContentHash == hash)
                {
                    skipped++;
                    continue;
                }

                var note = MarkdownNoteParser.Parse(content, Path.GetFileName(file));
                var chunks = MarkdownChunker.Chunk(
                    note.Body,
                    configuration.GetValue("Ingestion:MaxTokens", 500),
                    configuration.GetValue("Ingestion:OverlapTokens", 50));

                if (doc is null)
                {
                    doc = new KnowledgeDocument
                    {
                        KnowledgeSourceId = sourceId,
                        Title = note.Title,
                        UriReference = relative
                    };
                    db.Documents.Add(doc);
                }
                else
                {
                    doc.Title = note.Title;
                    db.Chunks.RemoveRange(doc.Chunks);
                }

                doc.RawContent = content;
                doc.ContentHash = hash;
                doc.IndexedAt = DateTimeOffset.UtcNow;

                doc.Chunks = chunks.Select((text, i) => new DocumentChunk
                {
                    KnowledgeDocumentId = doc.Id,
                    ChunkIndex = i,
                    TextContent = text
                }).ToList();

                await db.SaveChangesAsync(cancellationToken);

                foreach (var chunk in doc.Chunks)
                {
                    try
                    {
                        var vector = await embeddings.EmbedAsync(chunk.TextContent, cancellationToken);
                        await vectors.UpsertAsync(chunk.Id, doc.Id, sourceId, vector, embeddings.ModelId, cancellationToken);
                        chunksCreated++;
                    }
                    catch (EmbeddingProviderException ex)
                    {
                        logger.LogWarning("Embedding failed for chunk {ChunkId}: {Message}", chunk.Id, ex.Message);
                    }
                }
                processed++;
            }

            // Remove documents whose files disappeared from the vault.
            foreach (var (uri, doc) in existing)
            {
                if (seen.Contains(uri))
                    continue;
                await vectors.DeleteByDocumentAsync(doc.Id, cancellationToken);
                db.Documents.Remove(doc);
                removed++;
            }

            source.LastSyncAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            // RF-004: keep the FTS index consistent with Chunks after sync.
            await scope.ServiceProvider.GetRequiredService<ILexicalSearchService>()
                .ReconcileAsync(cancellationToken);

            return new SyncResultDto
            {
                Status = "completed",
                SourceId = sourceId,
                DocumentsProcessed = processed,
                DocumentsSkipped = skipped,
                DocumentsRemoved = removed,
                ChunksCreated = chunksCreated,
                DurationMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed for source {SourceId}", sourceId);
            return Fail(sourceId, "sync failed — see server logs", stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Index (or remove) a single file — used by the vault watcher.</summary>
    public async Task SyncFileAsync(Guid sourceId, string relativePath, CancellationToken cancellationToken = default)
    {
        var gate = SourceLocks.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            var vectors = scope.ServiceProvider.GetRequiredService<IVectorStore>();

            var source = await db.Sources.FindAsync([sourceId], cancellationToken);
            var root = source is null ? null : ResolveVaultRoot(source.ConfigurationJson);
            if (source is null || root is null)
                return;

            var full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return; // path traversal — ignore

            var doc = await db.Documents
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.KnowledgeSourceId == sourceId && d.UriReference == relativePath, cancellationToken);

            if (!File.Exists(full))
            {
                if (doc is not null)
                {
                    await vectors.DeleteByDocumentAsync(doc.Id, cancellationToken);
                    db.Documents.Remove(doc);
                    await db.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            var content = await File.ReadAllTextAsync(full, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            if (doc?.ContentHash == hash)
                return;

            var note = MarkdownNoteParser.Parse(content, Path.GetFileName(full));
            var chunks = MarkdownChunker.Chunk(
                note.Body,
                configuration.GetValue("Ingestion:MaxTokens", 500),
                configuration.GetValue("Ingestion:OverlapTokens", 50));

            if (doc is null)
            {
                doc = new KnowledgeDocument { KnowledgeSourceId = sourceId, Title = note.Title, UriReference = relativePath };
                db.Documents.Add(doc);
            }
            else
            {
                doc.Title = note.Title;
                db.Chunks.RemoveRange(doc.Chunks);
            }

            doc.RawContent = content;
            doc.ContentHash = hash;
            doc.IndexedAt = DateTimeOffset.UtcNow;
            doc.Chunks = chunks.Select((text, i) => new DocumentChunk
            {
                KnowledgeDocumentId = doc.Id,
                ChunkIndex = i,
                TextContent = text
            }).ToList();

            await db.SaveChangesAsync(cancellationToken);
            foreach (var chunk in doc.Chunks)
            {
                var vector = await embeddings.EmbedAsync(chunk.TextContent, cancellationToken);
                await vectors.UpsertAsync(chunk.Id, doc.Id, sourceId, vector, embeddings.ModelId, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<ILexicalSearchService>()
                .ReconcileAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static string? ResolveVaultRoot(string? configurationJson)
    {
        if (string.IsNullOrEmpty(configurationJson))
            return null;
        try
        {
            using var json = JsonDocument.Parse(configurationJson);
            var path = json.RootElement.TryGetProperty("path", out var p) ? p.GetString() : null;
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateMarkdown(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root, "*.md", options))
        {
            var relative = Path.GetRelativePath(root, file);
            if (IsExcluded(relative))
                continue;
            var info = new FileInfo(file);
            if (info.Length == 0 || info.Length > MaxFileBytes)
                continue;
            yield return file;
        }
    }

    private static bool IsExcluded(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s.StartsWith('.') || s.Equals(".obsidian", StringComparison.OrdinalIgnoreCase));
    }

    private static SyncResultDto Fail(Guid sourceId, string reason, double ms = 0) =>
        new() { Status = "failed", Reason = reason, SourceId = sourceId, DurationMs = ms };
}
