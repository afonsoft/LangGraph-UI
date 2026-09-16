namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Single-row chat provider override (SPEC-20260916-settings-chat-config RF-001):
/// endpoint + model for the OpenAI-compatible chat provider, editable from
/// /settings without redeploy. The API key is NOT stored here — it lives in
/// <see cref="IntegrationSecret"/> under the "chat" slug.
/// </summary>
public sealed class ChatSettings
{
    /// <summary>Single-row table — the service always upserts row Id = 1.</summary>
    public int Id { get; set; }
    /// <summary>OpenAI-compatible base URL (no /v1 suffix — the client appends it).</summary>
    public required string Endpoint { get; set; }
    /// <summary>Model name sent to the provider.</summary>
    public required string Model { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
