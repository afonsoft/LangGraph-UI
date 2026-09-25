using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Connectors.Cloud;
using KnowledgeHub.Server.Ingestion.Staging;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors;

/// <summary>
/// Shared-link Google Drive connector (SPEC-20260924-gdrive-shared-link-connector):
/// resolves the shared folder/file link, downloads/exports to local staging
/// through <see cref="GoogleDriveGateway"/>, and reuses the shared cloud loop
/// (glob/ext/size filters, incremental fingerprint dedup, extraction).
/// </summary>
internal sealed class GoogleDriveSharedConnector(
    GoogleDriveApiClient drive,
    IIntegrationSecretStore secrets,
    IStagingStorageService staging,
    ILogger<GoogleDriveSharedConnector> logger) : CloudConnectorBase(staging, logger)
{
    public override SourceType Type => SourceType.GoogleDrive;

    /// <summary>Secret-store slot for the optional Google API key.</summary>
    public static string SecretKey(Guid sourceId) => $"gdrive:{sourceId}";

    protected override async Task<IRemoteObjectGateway> CreateGatewayAsync(
        KnowledgeSource source, ConnectorConfig config, CancellationToken ct)
    {
        var url = config.String("sharedUrl")
            ?? throw new InvalidOperationException("GoogleDrive source requires 'sharedUrl'");
        if (!GoogleDriveApiClient.TryParseSharedUrl(url, out var resourceId, out var isFolder))
            throw new InvalidOperationException(
                $"'{url}' is not a recognized Google Drive share link (expected a /folders/ or /file/d/ URL)");

        var apiKey = await secrets.GetAsync(SecretKey(source.Id), ct);
        var maxFiles = config.Int("maxFiles", 200, 1, 1000);
        return new GoogleDriveGateway(drive, resourceId, isFolder, apiKey, maxFiles);
    }

    protected override string UriFor(ConnectorConfig config, string key) =>
        $"gdrive://{config.String("sharedUrl")}/{key}";
}
