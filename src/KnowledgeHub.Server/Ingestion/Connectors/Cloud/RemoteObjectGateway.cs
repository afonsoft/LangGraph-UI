namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>One remote object/file as listed by a cloud gateway
/// (SPEC-20260924-cloud-storage-connectors).</summary>
internal sealed record RemoteObject(string Key, string ETag, DateTimeOffset LastModified, long Size)
{
    /// <summary>Upstream change marker used as ContentHash for incremental dedup.</summary>
    public string Fingerprint => $"{ETag}|{LastModified.UtcDateTime:O}";
}

/// <summary>Read-only object listing/download abstraction over S3-compatible
/// and Azure Files backends — fakeable for tests.</summary>
internal interface IRemoteObjectGateway : IAsyncDisposable
{
    /// <summary>Bucket/share already bound at construction.</summary>
    IAsyncEnumerable<RemoteObject> ListAsync(string? prefix, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
}
