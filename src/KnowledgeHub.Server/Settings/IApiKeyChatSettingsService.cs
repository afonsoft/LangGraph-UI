using KnowledgeHub.Server.Chat;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Per-API-key chat provider settings with fallback to global defaults
/// (SPEC-20260916-api-key-settings RF-002 expanded for chat + integrations).
/// </summary>
public interface IApiKeyChatSettingsService
{
    /// <summary>Retorna as options efetivas para uma API key específica (override → global → env).</summary>
    ChatProviderOptions GetEffectiveOptions(Guid apiKeyId);

    /// <summary>Retorna o client das options efetivas, ou null quando provider=none.</summary>
    IChatClient? GetClient(Guid apiKeyId);

    /// <summary>Descarta o snapshot em cache para uma API key.</summary>
    void Invalidate(Guid apiKeyId);

    /// <summary>Retorna o estado efetivo mascarado para GET.</summary>
    Task<ApiKeyChatSettingsDto> DescribeAsync(Guid apiKeyId, CancellationToken cancellationToken = default);

    /// <summary>Salva ou atualiza o override de chat de uma API key.</summary>
    Task SaveAsync(Guid apiKeyId, string? endpoint, string? model, string? apiKey, CancellationToken cancellationToken = default);

    /// <summary>Salva a API key de uma integração para uma API key específica.</summary>
    Task SaveIntegrationKeyAsync(Guid apiKeyId, string provider, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Remove a API key de uma integração para uma API key específica.</summary>
    Task RemoveIntegrationKeyAsync(Guid apiKeyId, string provider, CancellationToken cancellationToken = default);

    /// <summary>Retorna o secret efetivo (per-key → global).</summary>
    Task<string?> GetIntegrationSecretAsync(Guid apiKeyId, string provider, CancellationToken cancellationToken = default);

    /// <summary>Remove o override de uma API key.</summary>
    Task RemoveAsync(Guid apiKeyId, CancellationToken cancellationToken = default);
}
