namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>One remote object/file as listed by a cloud gateway
/// (SPEC-20260924-cloud-storage-connectors).</summary>
internal sealed record RemoteObject(string Key, string ETag, DateTimeOffset LastModified, long Size)
{
    /// <summary>Upstream change marker used as ContentHash for incremental dedup.
    /// SPEC-20260926-ingestion-connector-integrity RF-005: empty when the listing
    /// cannot prove change (no ETag AND no mtime — e.g. scraped public folders) —
    /// an empty fingerprint always counts as changed, never as unchanged.</summary>
    public string Fingerprint => ETag.Length == 0 && LastModified == DateTimeOffset.MinValue
        ? ""
        : $"{ETag}|{LastModified.UtcDateTime:O}";
}

/// <summary>Read-only object listing/download abstraction over S3-compatible
/// and Azure Files backends — fakeable for tests.</summary>
internal interface IRemoteObjectGateway : IAsyncDisposable
{
    /// <summary>Bucket/share already bound at construction.</summary>
    IAsyncEnumerable<RemoteObject> ListAsync(string? prefix, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>True when the listing was cut short by a provider-side cap
    /// (e.g. maxFiles) — unseen items must not be treated as deleted
    /// (SPEC-20260926-ingestion-connector-integrity RF-002).</summary>
    bool Truncated => false;
}
