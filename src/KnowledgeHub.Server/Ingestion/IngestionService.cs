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
using Microsoft.Extensions.DependencyInjection;

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
    IEnumerable<Connectors.ISourceConnector> connectors,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache cache,
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
            {
                // SPEC-20260914-webpage-docfile-connectors RF-003: route to the
                // typed connector, then run the shared dedup→chunk→embed pipeline.
                var connector = connectors.FirstOrDefault(c => c.Type == source.SourceType);
                if (connector is null)
                {
                    var skipReason = $"connector {source.SourceType} not implemented";
                    source.LastSyncStatus = "skipped";
                    source.LastError = skipReason;
                    await db.SaveChangesAsync(cancellationToken);
                    return new SyncResultDto { Status = "skipped", Reason = skipReason, SourceId = sourceId, DurationMs = stopwatch.Elapsed.TotalMilliseconds };
                }

                return await SyncViaConnectorAsync(source, connector, db, vectors, scope, stopwatch, cancellationToken);
            }

            var root = ResolveVaultRoot(source.ConfigurationJson);
            if (root is null || !Directory.Exists(root))
            {
                // SPEC-20260914-obsidian-webdav RF-002: a missing vault path usually
                // means an unmounted remote — surface that on the source record.
                var reason = $"vault path '{root ?? "(not configured)"}' not found — mount unavailable?";
                source.LastSyncStatus = "failed";
                source.LastError = reason;
                await db.SaveChangesAsync(cancellationToken);
                return Fail(sourceId, reason);
            }

            var files = EnumerateMarkdown(root);
            // SPEC-20260916-performance-memory-cache RF-004: no Include(Chunks) —
            // the old code materialized every chunk's text + embedding BLOB for
            // the whole source. Changed docs purge via ExecuteDeleteAsync below.
            var existing = await db.Documents
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
                    // Purge old chunks with a direct DELETE — no BLOB/text
                    // materialization, no tracked-collection pitfalls.
                    await db.Chunks.Where(c => c.KnowledgeDocumentId == doc.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                doc.RawContent = content;
                doc.ContentHash = hash;
                doc.IndexedAt = DateTimeOffset.UtcNow;

                // AddRange via DbSet — reassigning doc.Chunks after RemoveRange makes EF Core
                // emit an UPDATE for the deleted rows inside the same batch (concurrency error).
                var newChunks = chunks.Select((text, i) => new DocumentChunk
                {
                    KnowledgeDocumentId = doc.Id,
                    ChunkIndex = i,
                    TextContent = text
                }).ToList();
                db.Chunks.AddRange(newChunks);

                await db.SaveChangesAsync(cancellationToken);
                chunksCreated += await EmbedChunksAsync(newChunks, doc.Id, sourceId, vectors, cancellationToken);
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
            source.LastSyncStatus = "completed";
            source.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            // RF-004: keep the FTS index consistent with Chunks after sync.
            await scope.ServiceProvider.GetRequiredService<ILexicalSearchService>()
                .ReconcileAsync(cancellationToken);
            await BumpIndexVersionAsync(cancellationToken);

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
            await TryRecordSyncFailureAsync(sourceId, ex.Message);
            return Fail(sourceId, "sync failed — see server logs", stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Shared pipeline for non-vault connectors: dedup by content hash, chunk,
    /// embed, remove documents no longer returned by the fetch.
    /// </summary>
    private async Task<SyncResultDto> SyncViaConnectorAsync(
        KnowledgeSource source,
        Connectors.ISourceConnector connector,
        KnowledgeHubDbContext db,
        IVectorStore vectors,
        AsyncServiceScope scope,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var fetch = await connector.FetchAsync(source, cancellationToken);

        var existing = await db.Documents
            .Where(d => d.KnowledgeSourceId == source.Id)
            .ToDictionaryAsync(d => d.UriReference, cancellationToken);

        var processed = 0; var skipped = 0; var removed = 0; var chunksCreated = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in fetch.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(raw.UriReference);

            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw.TextContent)));
            if (existing.TryGetValue(raw.UriReference, out var doc) && doc.ContentHash == hash)
            {
                skipped++;
                continue;
            }

            if (doc is null)
            {
                doc = new KnowledgeDocument
                {
                    KnowledgeSourceId = source.Id,
                    Title = raw.Title,
                    UriReference = raw.UriReference
                };
                db.Documents.Add(doc);
            }
            else
            {
                doc.Title = raw.Title;
                await db.Chunks.Where(c => c.KnowledgeDocumentId == doc.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            doc.RawContent = raw.TextContent;
            doc.ContentHash = hash;
            doc.IndexedAt = DateTimeOffset.UtcNow;

            // AddRange via DbSet — see vault path above; nav reassignment after
            // RemoveRange produces a bogus UPDATE inside the same SaveChanges batch.
            var newChunks = MarkdownChunker.Chunk(
                raw.TextContent,
                configuration.GetValue("Ingestion:MaxTokens", 500),
                configuration.GetValue("Ingestion:OverlapTokens", 50))
                .Select((text, i) => new DocumentChunk
                {
                    KnowledgeDocumentId = doc.Id,
                    ChunkIndex = i,
                    TextContent = text
                }).ToList();
            db.Chunks.AddRange(newChunks);

            await db.SaveChangesAsync(cancellationToken);
            chunksCreated += await EmbedChunksAsync(newChunks, doc.Id, source.Id, vectors, cancellationToken);
            processed++;
        }

        foreach (var (uri, doc) in existing)
        {
            if (seen.Contains(uri))
                continue;
            await vectors.DeleteByDocumentAsync(doc.Id, cancellationToken);
            db.Documents.Remove(doc);
            removed++;
        }

        source.LastSyncAt = DateTimeOffset.UtcNow;
        source.LastSyncStatus = "completed";
        source.LastError = fetch.Warnings.Count == 0 ? null
            : $"{fetch.Warnings.Count} item(s) skipped: {string.Join("; ", fetch.Warnings.Take(5))}";
        await db.SaveChangesAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ILexicalSearchService>()
            .ReconcileAsync(cancellationToken);
        await BumpIndexVersionAsync(cancellationToken);

        return new SyncResultDto
        {
            Status = "completed",
            SourceId = source.Id,
            DocumentsProcessed = processed,
            DocumentsSkipped = skipped,
            DocumentsRemoved = removed,
            ChunksCreated = chunksCreated,
            DurationMs = stopwatch.Elapsed.TotalMilliseconds,
            Reason = fetch.Warnings.Count == 0 ? null : $"{fetch.Warnings.Count} item(s) skipped: {string.Join("; ", fetch.Warnings.Take(5))}"
        };
    }

    /// <summary>Embed + upsert chunks in one batch — a single provider call for
    /// N texts and a single store batch (SPEC-20260916-performance-memory-cache
    /// RF-004). Batch failure falls back to per-chunk so one bad text doesn't
    /// sink the document.</summary>
    private async Task<int> EmbedChunksAsync(
        List<DocumentChunk> chunks, Guid documentId, Guid sourceId,
        IVectorStore vectors, CancellationToken cancellationToken)
    {
        IReadOnlyList<float[]> batchVectors;
        try
        {
            batchVectors = await embeddings.EmbedBatchAsync(
                chunks.Select(c => c.TextContent).ToList(), cancellationToken);
        }
        catch (EmbeddingProviderException ex)
        {
            logger.LogWarning("Batch embedding failed for document {DocumentId}, falling back to per-chunk: {Message}",
                documentId, ex.Message);
            return await EmbedChunksIndividuallyAsync(chunks, documentId, sourceId, vectors, cancellationToken);
        }

        try
        {
            var items = chunks.Zip(batchVectors)
                .Select(pair => new VectorUpsert(pair.First.Id, documentId, sourceId, pair.Second))
                .ToList();
            await vectors.UpsertBatchAsync(items, embeddings.ModelId, cancellationToken);
            return items.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Batch vector upsert failed for document {DocumentId}, falling back to per-chunk: {Message}",
                documentId, ex.Message);
            return await EmbedChunksIndividuallyAsync(chunks, documentId, sourceId, vectors, cancellationToken);
        }
    }

    private async Task<int> EmbedChunksIndividuallyAsync(
        List<DocumentChunk> chunks, Guid documentId, Guid sourceId,
        IVectorStore vectors, CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var chunk in chunks)
        {
            try
            {
                var vector = await embeddings.EmbedAsync(chunk.TextContent, cancellationToken);
                await vectors.UpsertAsync(chunk.Id, documentId, sourceId, vector, embeddings.ModelId, cancellationToken);
                created++;
            }
            catch (EmbeddingProviderException ex)
            {
                logger.LogWarning("Embedding failed for chunk {ChunkId}: {Message}", chunk.Id, ex.Message);
            }
        }
        return created;
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
            if (source is null)
                return;

            var isDocFile = source.SourceType == SourceType.DocumentFile;
            var root = ResolveVaultRoot(source.ConfigurationJson);
            // RF-002: a missing root means the mount is down (vault dir) — record it
            // and bail without touching documents. DocumentFile may point at a file.
            var rootExists = root is not null &&
                (isDocFile ? Directory.Exists(root) || File.Exists(root) : Directory.Exists(root));
            if (!rootExists)
            {
                if (source.LastSyncStatus != "failed")
                {
                    source.LastSyncStatus = "failed";
                    source.LastError = $"vault path '{root ?? "(not configured)"}' not found — mount unavailable?";
                    await db.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            // Clear a previous failure once the mount is reachable again.
            if (source.LastSyncStatus == "failed")
            {
                source.LastSyncStatus = "completed";
                source.LastError = null;
                await db.SaveChangesAsync(cancellationToken);
            }

            if (root is null)
                return;

            var full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return; // path traversal — ignore

            var doc = await db.Documents
                .FirstOrDefaultAsync(d => d.KnowledgeSourceId == sourceId && d.UriReference == relativePath, cancellationToken);

            if (!File.Exists(full)
                || isDocFile && !Connectors.DocumentFileConnector.SupportedExtensions.Contains(Path.GetExtension(full)))
            {
                if (doc is not null)
                {
                    await vectors.DeleteByDocumentAsync(doc.Id, cancellationToken);
                    db.Documents.Remove(doc);
                    await db.SaveChangesAsync(cancellationToken);
                    await scope.ServiceProvider.GetRequiredService<ILexicalSearchService>()
                        .ReconcileAsync(cancellationToken);
                    await BumpIndexVersionAsync(cancellationToken);
                }
                return;
            }

            // DocumentFile hashes/extracts the *text*; vault hashes the raw file.
            string content;
            string title;
            string body;
            if (isDocFile)
            {
                content = await Connectors.DocumentFileConnector.ExtractTextAsync(
                    full, Path.GetExtension(full), cancellationToken) ?? "";
                if (content.Length == 0)
                    return;
                title = Path.GetFileNameWithoutExtension(full);
                body = content;
            }
            else
            {
                content = await File.ReadAllTextAsync(full, cancellationToken);
                var note = MarkdownNoteParser.Parse(content, Path.GetFileName(full));
                title = note.Title;
                body = note.Body;
            }

            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            if (doc?.ContentHash == hash)
                return;

            var chunks = MarkdownChunker.Chunk(
                body,
                configuration.GetValue("Ingestion:MaxTokens", 500),
                configuration.GetValue("Ingestion:OverlapTokens", 50));

            if (doc is null)
            {
                doc = new KnowledgeDocument { KnowledgeSourceId = sourceId, Title = title, UriReference = relativePath };
                db.Documents.Add(doc);
            }
            else
            {
                doc.Title = title;
                await db.Chunks.Where(c => c.KnowledgeDocumentId == doc.Id)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            doc.RawContent = content;
            doc.ContentHash = hash;
            doc.IndexedAt = DateTimeOffset.UtcNow;

            // AddRange via DbSet — see vault path above.
            var newChunks = chunks.Select((text, i) => new DocumentChunk
            {
                KnowledgeDocumentId = doc.Id,
                ChunkIndex = i,
                TextContent = text
            }).ToList();
            db.Chunks.AddRange(newChunks);

            await db.SaveChangesAsync(cancellationToken);
            await EmbedChunksAsync(newChunks, doc.Id, sourceId, vectors, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<ILexicalSearchService>()
                .ReconcileAsync(cancellationToken);
            await BumpIndexVersionAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Bump the index-version token so cached search results keyed on
    /// it miss after this sync (SPEC-20260916-performance-memory-cache §5).
    /// Best-effort — a down cache must never fail a sync.</summary>
    private async Task BumpIndexVersionAsync(CancellationToken cancellationToken) =>
        await Caching.SafeCache.SetStringAsync(cache, Caching.CacheKeys.IndexVersion,
            Guid.NewGuid().ToString("N"), TimeSpan.FromDays(7), logger, cancellationToken);

    /// <summary>Best-effort: record a sync failure on the source in a fresh scope.</summary>
    private async Task TryRecordSyncFailureAsync(Guid sourceId, string message)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
            var source = await db.Sources.FindAsync([sourceId]);
            if (source is null)
                return;
            source.LastSyncStatus = "failed";
            source.LastError = message.Length > 1000 ? message[..1000] : message;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record sync failure on source {SourceId}", sourceId);
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
