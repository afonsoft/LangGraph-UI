using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Staging;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>OCI Object Storage connector (SPEC-20260924-cloud-storage-connectors
/// RF-005) — S3-compatible endpoint at
/// <c>{namespace}.compat.objectstorage.{region}.oraclecloud.com</c>.</summary>
internal sealed class OciStorageConnector(
    IIntegrationSecretStore secrets,
    IStagingStorageService staging,
    ILogger<OciStorageConnector> logger) : CloudConnectorBase(staging, logger)
{
    public override SourceType Type => SourceType.OciStorage;

    /// <summary>Secret-store slot for the OCI customer secret key.</summary>
    public static string SecretKey(Guid sourceId) => $"oci:{sourceId}";


    protected override async Task<IRemoteObjectGateway> CreateGatewayAsync(
        KnowledgeSource source, ConnectorConfig config, CancellationToken ct)
    {
        var ns = config.String("namespace")
            ?? throw new InvalidOperationException("OciStorage source requires 'namespace'");
        var region = config.String("region")
            ?? throw new InvalidOperationException("OciStorage source requires 'region'");
        var bucket = config.String("bucketName")
            ?? throw new InvalidOperationException("OciStorage source requires 'bucketName'");
        var accessKeyId = config.String("accessKeyId")
            ?? throw new InvalidOperationException("OciStorage source requires 'accessKeyId'");
        var secretAccessKey = await secrets.GetAsync(SecretKey(source.Id), ct)
            ?? throw new InvalidOperationException(
                "OciStorage source has no stored secretAccessKey — re-save the source with credentials");

        return S3ObjectGateway.ForOci(bucket, ns, region, accessKeyId, secretAccessKey);
    }

    protected override string UriFor(ConnectorConfig config, string key) =>
        $"oci://{config.String("bucketName")}/{key}";
}
