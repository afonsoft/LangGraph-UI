using System.Net.Http.Json;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Client.Services;

/// <summary>Typed client for /api/settings (SPEC-20260916-firecrawl-mcp-proxy).</summary>
public sealed class SettingsApiClient(HttpClient http)
{
    /// <summary>Lista as integrações com o status mascarado de cada key.</summary>
    public Task<IntegrationSettingsResponse?> ListIntegrationsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<IntegrationSettingsResponse>("api/settings/integrations", ct);

    /// <summary>Salva/substitui a API key de uma integração.</summary>
    public async Task<ApiResult<object>> SetKeyAsync(string provider, string apiKey, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            $"api/settings/integrations/{Uri.EscapeDataString(provider)}",
            new SetIntegrationKeyRequest { ApiKey = apiKey }, ct);
        return await ReadAsync<object>(response, ct);
    }

    /// <summary>Remove a key armazenada de uma integração.</summary>
    public async Task<ApiResult<object>> RemoveKeyAsync(string provider, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/settings/integrations/{Uri.EscapeDataString(provider)}", ct);
        return await ReadAsync<object>(response, ct);
    }

    // SPEC-20260916-settings-chat-config: chat provider card endpoints.

    /// <summary>Obtém a configuração efetiva de chat (mascarada, sem a key).</summary>
    public Task<ChatSettingsDto?> GetChatAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<ChatSettingsDto>("api/settings/chat", ct);

    /// <summary>Salva endpoint/model do chat e, se informada, a API key.</summary>
    public async Task<ApiResult<object>> SaveChatAsync(string endpoint, string model, string? apiKey, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync("api/settings/chat",
            new SaveChatSettingsRequest { Endpoint = endpoint, Model = model, ApiKey = apiKey }, ct);
        return await ReadAsync<object>(response, ct);
    }

    /// <summary>Remove apenas a key de chat armazenada (env volta a valer).</summary>
    public async Task<ApiResult<object>> RemoveChatKeyAsync(CancellationToken ct = default)
    {
        var response = await http.DeleteAsync("api/settings/chat/apikey", ct);
        return await ReadAsync<object>(response, ct);
    }

    /// <summary>Apaga a configuração persistida de chat, voltando às variáveis de ambiente.</summary>
    public async Task<ApiResult<object>> ClearChatAsync(CancellationToken ct = default)
    {
        var response = await http.DeleteAsync("api/settings/chat", ct);
        return await ReadAsync<object>(response, ct);
    }

    /// <summary>Testa a conexão com o provider usando os valores informados, sem persistir nada.</summary>
    public async Task<ApiResult<TestChatConnectionResponse>> TestChatAsync(TestChatConnectionRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/settings/chat/test", request, ct);
        return await ReadAsync<TestChatConnectionResponse>(response, ct);
    }

    // SPEC-20260916-api-key-settings: per-API-key chat provider settings.

    /// <summary>Obtém a configuração efetiva de chat para uma API key específica.</summary>
    public Task<ApiKeyChatSettingsDto?> GetApiKeyChatAsync(Guid apiKeyId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<ApiKeyChatSettingsDto>($"api/api-keys/{apiKeyId}/settings/chat", ct);

    /// <summary>Salva endpoint/model/chat key para uma API key específica.</summary>
    public async Task<ApiResult<object>> SaveApiKeyChatAsync(Guid apiKeyId, string? endpoint, string? model, string? apiKey, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"api/api-keys/{apiKeyId}/settings/chat",
            new SaveApiKeyChatSettingsRequest { Endpoint = endpoint, Model = model, ApiKey = apiKey }, ct);
        return await ReadAsync<object>(response, ct);
    }

    /// <summary>Remove o override de chat de uma API key.</summary>
    public async Task<ApiResult<object>> RemoveApiKeyChatAsync(Guid apiKeyId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/api-keys/{apiKeyId}/settings/chat", ct);
        return await ReadAsync<object>(response, ct);
    }

    /// <summary>Lê a resposta HTTP num ApiResult: desserializa o corpo em sucesso
    /// e extrai o campo "error" (ou o status HTTP) em falha.</summary>
    private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            T? value = default;
            try { value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct); }
            catch { /* 204 or non-JSON body */ }
            return new ApiResult<T>(value, null);
        }

        string? error = null;
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(cancellationToken: ct);
            if (doc is not null && doc.TryGetValue("error", out var e))
                error = e.GetString();
        }
        catch { /* non-JSON error body */ }

        return new ApiResult<T>(default, error ?? $"HTTP {(int)response.StatusCode}");
    }
}
