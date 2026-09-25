using System.Text.Json.Serialization;

namespace KnowledgeHub.Shared.Contracts;

/// <summary>Supported knowledge source connector types (SPEC-02 RF-001).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceType
{
    WebPage = 1,
    ObsidianVault = 2,
    DocumentFile = 3,
    RestApi = 4,
    SqlDatabase = 5,
    /// <summary>Upstream MCP server exposed through the tool catalog
    /// (SPEC-20260917-mcp-proxy-source-type) — proxy only, not ingestible.</summary>
    McpProxy = 6,
    /// <summary>Notion workspace via REST API internal integration
    /// (SPEC-20260919-notion-connector) — read-only ingestion.</summary>
    Notion = 7,
    /// <summary>AWS S3 bucket via AWS SDK (SPEC-20260924-cloud-storage-connectors).</summary>
    AwsS3 = 8,
    /// <summary>Azure Files share via Azure.Storage.Files.Shares.</summary>
    AzureFiles = 9,
    /// <summary>OCI Object Storage via the S3-compatible endpoint.</summary>
    OciStorage = 10,
    /// <summary>Shared Google Drive folder/file link
    /// (SPEC-20260924-gdrive-shared-link-connector).</summary>
    GoogleDrive = 11
}
