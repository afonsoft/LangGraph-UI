namespace KnowledgeHub.Server.Domain.Entities;

/// <summary>
/// Per-API-key chat provider override (SPEC-20260916-api-key-settings RF-001):
/// endpoint + model optional overrides editable from the API keys page.
/// The API key secret (if overridden) is stored in IntegrationSecret under
/// slug <c>apikey-chat-{apiKeyId}</c>, NOT in this entity.
/// </summary>
public sealed class ApiKeyChatSettings
{
    public int Id { get; set; }
    public Guid ApiKeyId { get; set; }
    public ApiKey ApiKey { get; set; } = null!;
    /// <summary>OpenAI-compatible base URL override, null = inherit from global.</summary>
    public string? Endpoint { get; set; }
    /// <summary>Model name override, null = inherit from global.</summary>
    public string? Model { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
