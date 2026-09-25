namespace KnowledgeHub.Shared.Contracts;

/// <summary>
/// SPEC-20260926-ingestion-connector-integrity RF-004: decides whether an Azure
/// Files credential is a full connection string or a bare account key. Base64
/// account keys legitimately end with <c>=</c>/<c>==</c> padding, so a plain
/// "contains =" check misroutes them into the connectionString slot and the
/// ShareClient ctor throws.
/// </summary>
public static class AzureCredentialClassifier
{
    /// <summary>True when the value carries connection-string markers
    /// (semicolon-delimited assignments or a SAS token); a bare account key —
    /// even one padded with <c>=</c> — returns false.</summary>
    public static bool LooksLikeConnectionString(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains(';')
            || value.Contains("AccountName=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("DefaultEndpointsProtocol=", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase));
}
