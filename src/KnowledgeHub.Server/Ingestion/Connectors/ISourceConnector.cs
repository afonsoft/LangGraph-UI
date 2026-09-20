using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors;

/// <summary>One fetched document before chunking (SPEC-20260914-webpage-docfile-connectors RF-003).
/// <see cref="Fingerprint"/> is an optional upstream change marker (e.g. Notion's
/// <c>last_edited_time</c>) — when present it replaces the content hash for dedup
/// (SPEC-20260919-notion-connector RF-007).</summary>
public sealed record RawDocument(string UriReference, string Title, string TextContent, string? Fingerprint = null);

/// <summary>Connector output: fetched documents plus per-item warnings (skipped files/pages).</summary>
public sealed record FetchResult(IReadOnlyList<RawDocument> Documents, IReadOnlyList<string> Warnings);

/// <summary>
/// Pulls raw documents out of a source configuration. IngestionService routes
/// <see cref="SourceType"/> to a connector and feeds the results through the
/// shared dedup → chunk → embed pipeline.
/// </summary>
public interface ISourceConnector
{
    SourceType Type { get; }

    /// <summary>Fetches every reachable document; per-item failures land in <see cref="FetchResult.Warnings"/>.</summary>
    Task<FetchResult> FetchAsync(KnowledgeSource source, CancellationToken cancellationToken);
}

/// <summary>
/// Optional connector extension (SPEC-20260919-notion-connector RF-007): receives
/// the stored <c>UriReference → ContentHash</c> map so the connector can skip
/// re-fetching unchanged remote content (e.g. Notion pages whose
/// <c>last_edited_time</c> still matches). Unchanged items are emitted as
/// <see cref="RawDocument"/> with empty <see cref="RawDocument.TextContent"/> and
/// their previous fingerprint so the URI stays in <c>seen</c> and the document
/// survives reconciliation.
/// </summary>
public interface IIncrementalSourceConnector : ISourceConnector
{
    Task<FetchResult> FetchAsync(
        KnowledgeSource source,
        IReadOnlyDictionary<string, string> existingFingerprints,
        CancellationToken cancellationToken);
}
