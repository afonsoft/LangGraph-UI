using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Staging;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>
/// Shared fetch loop for remote object-storage connectors
/// (SPEC-20260924-cloud-storage-connectors RF-003..RF-005): list → glob/ext/size
/// filters → incremental fingerprint dedup → download to staging → extract →
/// emit <see cref="RawDocument"/> with an <c>{etag}|{lastModified}</c>
/// fingerprint so unchanged objects skip re-download and re-chunk.
/// </summary>
internal abstract class CloudConnectorBase(IStagingStorageService staging, ILogger logger)
    : IIncrementalSourceConnector
{
    public abstract SourceType Type { get; }

    /// <summary>Builds the authenticated gateway for this source — secrets come
    /// from the store inside this method (never from raw config).</summary>
    protected abstract Task<IRemoteObjectGateway> CreateGatewayAsync(
        KnowledgeSource source, ConnectorConfig config, CancellationToken ct);

    /// <summary>Canonical document URI (e.g. <c>s3://bucket/key</c>).</summary>
    protected abstract string UriFor(ConnectorConfig config, string key);

    /// <summary>Configurable for tests.</summary>
    internal Func<KnowledgeSource, ConnectorConfig, CancellationToken, Task<IRemoteObjectGateway>>? GatewayOverride { get; set; }

    public Task<FetchResult> FetchAsync(KnowledgeSource source, CancellationToken cancellationToken) =>
        FetchAsync(source, new Dictionary<string, string>(), cancellationToken);

    public async Task<FetchResult> FetchAsync(
        KnowledgeSource source,
        IReadOnlyDictionary<string, string> existingFingerprints,
        CancellationToken cancellationToken)
    {
        var config = ConnectorConfig.Parse(source.ConfigurationJson);
        var glob = config.String("glob") ?? "**/*";
        var matcher = DocumentFileConnector.GlobMatcher.Compile(glob);
        var maxBytes = config.Int("maxFileSizeMB", 20, 1, 512) * 1024L * 1024;
        var prefix = config.String("prefix") ?? config.String("directoryPath");

        await using var gateway = GatewayOverride is not null
            ? await GatewayOverride(source, config, cancellationToken)
            : await CreateGatewayAsync(source, config, cancellationToken);

        var documents = new List<RawDocument>();
        var warnings = new List<string>();
        var stagingDir = staging.GetStagingDirectory(source.Id);

        await foreach (var obj in gateway.ListAsync(prefix, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = UriFor(config, obj.Key);
            try
            {
                if (!matcher(obj.Key))
                    continue;
                var ext = Path.GetExtension(obj.Key);
                if (!DocumentFileConnector.SupportedExtensions.Contains(ext))
                {
                    warnings.Add($"{obj.Key}: unsupported extension '{ext}'");
                    continue;
                }
                if (obj.Size == 0 || obj.Size > maxBytes)
                {
                    warnings.Add($"{obj.Key}: size {obj.Size} outside 1..{maxBytes} bytes");
                    continue;
                }

                // Incremental: same upstream marker → keep stored doc, no download.
                if (existingFingerprints.TryGetValue(uri, out var stored)
                    && string.Equals(stored, obj.Fingerprint, StringComparison.Ordinal))
                {
                    documents.Add(new RawDocument(uri, Path.GetFileNameWithoutExtension(obj.Key), "", obj.Fingerprint));
                    continue;
                }

                var localPath = Path.Combine(stagingDir, Sanitize(obj.Key));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await using (var remote = await gateway.OpenReadAsync(obj.Key, cancellationToken))
                    await using (var local = File.Create(localPath))
                    {
                        await remote.CopyToAsync(local, cancellationToken);
                    }

                    var text = await DocumentFileConnector.ExtractTextAsync(localPath, ext, cancellationToken);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        warnings.Add($"{obj.Key}: no extractable text");
                        continue;
                    }
                    documents.Add(new RawDocument(uri, Path.GetFileNameWithoutExtension(obj.Key), text, obj.Fingerprint));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    TryDeletePartial(localPath);
                    logger.LogWarning(ex, "Failed to download/extract {Key} — skipped", obj.Key);
                    warnings.Add($"{obj.Key}: {ex.Message}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to process {Key} — skipped", obj.Key);
                warnings.Add($"{obj.Key}: {ex.Message}");
            }
        }

        return new FetchResult(documents, warnings);
    }

    /// <summary>Maps a remote key to a safe local relative path (no traversal).</summary>
    private static string Sanitize(string key)
    {
        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(parts.Select(p => p is "." or ".." ? "_" : p).ToArray());
    }

    private void TryDeletePartial(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to remove partial download {Path}", path);
        }
    }
}
