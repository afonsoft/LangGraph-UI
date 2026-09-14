using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors;

/// <summary>One fetched document before chunking (SPEC-20260914-webpage-docfile-connectors RF-003).</summary>
public sealed record RawDocument(string UriReference, string Title, string TextContent);

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
