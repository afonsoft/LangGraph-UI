using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Staging;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Ingestion.Connectors.Cloud;

/// <summary>AWS S3 connector (SPEC-20260924-cloud-storage-connectors RF-003):
/// lists objects with pagination, downloads to staging, extracts text.</summary>
internal sealed class AwsS3Connector(
    IIntegrationSecretStore secrets,
    IStagingStorageService staging,
    ILogger<AwsS3Connector> logger) : CloudConnectorBase(staging, logger)
{
    public override SourceType Type => SourceType.AwsS3;

    /// <summary>Secret-store slot for the access-secret pair.</summary>
    public static string SecretKey(Guid sourceId) => $"s3:{sourceId}";


    protected override async Task<IRemoteObjectGateway> CreateGatewayAsync(
        KnowledgeSource source, ConnectorConfig config, CancellationToken ct)
    {
        var bucket = config.String("bucketName")
            ?? throw new InvalidOperationException("AwsS3 source requires 'bucketName'");
        var region = config.String("region")
            ?? throw new InvalidOperationException("AwsS3 source requires 'region'");
        var accessKeyId = config.String("accessKeyId")
            ?? throw new InvalidOperationException("AwsS3 source requires 'accessKeyId'");
        var secretAccessKey = await secrets.GetAsync(SecretKey(source.Id), ct)
            ?? throw new InvalidOperationException(
                "AwsS3 source has no stored secretAccessKey — re-save the source with credentials");

        return S3ObjectGateway.ForAws(bucket, region, accessKeyId, secretAccessKey);
    }

    protected override string UriFor(ConnectorConfig config, string key) =>
        $"s3://{config.String("bucketName")}/{key}";
}
