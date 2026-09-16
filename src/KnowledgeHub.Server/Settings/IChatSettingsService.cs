using KnowledgeHub.Server.Chat;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Effective chat-provider configuration and runtime client
/// (SPEC-20260916-settings-chat-config RF-002/RF-003). A stored
/// <c>ChatSettings</c> row overrides env <c>Chat:*</c> for endpoint/model and
/// implies provider <c>openai</c>; the API key resolves store → env. Mutations
/// call <see cref="Invalidate"/> so the next <see cref="GetClient"/> rebuilds —
/// no restart required.
/// </summary>
public interface IChatSettingsService
{
    /// <summary>Effective options (store-over-env merged). Snapshot-cached.</summary>
    ChatProviderOptions GetEffectiveOptions();

    /// <summary>Client for the effective options, or null when provider=none.
    /// Never throws on missing config — callers keep their null-tolerance.</summary>
    IChatClient? GetClient();

    /// <summary>Drops the cached snapshot; next access re-reads the store.</summary>
    void Invalidate();

    /// <summary>Masked effective state for GET /api/settings/chat — never the secret.</summary>
    Task<ChatSettingsDto> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts the single ChatSettings row; stores the key when
    /// <paramref name="apiKey"/> is non-blank (blank keeps the stored key).</summary>
    Task SaveAsync(string endpoint, string model, string? apiKey, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored "chat" key only — env key falls back.</summary>
    Task RemoveKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the ChatSettings row AND the stored key — full env fallback.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Probes GET {endpoint}/v1/models with the resolvable key;
    /// blank request fields fall back to the effective config. Persists nothing.</summary>
    Task<TestChatConnectionResponse> TestAsync(TestChatConnectionRequest request, CancellationToken cancellationToken = default);
}
