using System.Text.Json;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Staging;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>Azure Files share connector (SPEC-20260924-cloud-storage-connectors
/// RF-004) — recursive share walk, staging download, text extraction.</summary>
internal sealed class AzureFilesConnector(
    IIntegrationSecretStore secrets,
    IStagingStorageService staging,
    ILogger<AzureFilesConnector> logger) : CloudConnectorBase(staging, logger)
{
    public override SourceType Type => SourceType.AzureFiles;

    /// <summary>Secret-store slot — JSON payload with
    /// <c>connectionString</c>/<c>accountKey</c> as provided.</summary>
    public static string SecretKey(Guid sourceId) => $"azure:{sourceId}";


    /// <summary>Auth payload stored under <see cref="SecretKey"/>.</summary>
    internal sealed record AzureSecret(string? ConnectionString, string? AccountName, string? AccountKey)
    {
        public static AzureSecret? FromJson(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                return new AzureSecret(
                    r.TryGetProperty("connectionString", out var cs) ? cs.GetString() : null,
                    r.TryGetProperty("accountName", out var an) ? an.GetString() : null,
                    r.TryGetProperty("accountKey", out var ak) ? ak.GetString() : null);
            }
            catch (JsonException)
            {
                return new AzureSecret(json, null, null); // legacy: raw connection string
            }
        }
    }

    protected override async Task<IRemoteObjectGateway> CreateGatewayAsync(
        KnowledgeSource source, ConnectorConfig config, CancellationToken ct)
    {
        var shareName = config.String("shareName")
            ?? throw new InvalidOperationException("AzureFiles source requires 'shareName'");
        var secret = AzureSecret.FromJson(await secrets.GetAsync(SecretKey(source.Id), ct))
            ?? throw new InvalidOperationException(
                "AzureFiles source has no stored credentials — re-save with a connection string or account key");

        return AzureShareGateway.Create(
            shareName,
            config.String("directoryPath"),
            secret.ConnectionString,
            secret.AccountName ?? config.String("accountName"),
            secret.AccountKey);
    }

    protected override string UriFor(ConnectorConfig config, string key) =>
        $"azure://{config.String("shareName")}/{key}";
}
