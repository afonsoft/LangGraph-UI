using System.Net.Http.Json;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Client.Services;

/// <summary>Typed client for /api/settings (SPEC-20260916-firecrawl-mcp-proxy).</summary>
public sealed class SettingsApiClient(HttpClient http)
{
    public Task<IntegrationSettingsResponse?> ListIntegrationsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<IntegrationSettingsResponse>("api/settings/integrations", ct);

    public async Task<ApiResult<object>> SetKeyAsync(string provider, string apiKey, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync(
            $"api/settings/integrations/{Uri.EscapeDataString(provider)}",
            new SetIntegrationKeyRequest { ApiKey = apiKey }, ct);
        return await ReadAsync(response, ct);
    }

    public async Task<ApiResult<object>> RemoveKeyAsync(string provider, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/settings/integrations/{Uri.EscapeDataString(provider)}", ct);
        return await ReadAsync(response, ct);
    }

    private static async Task<ApiResult<object>> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return new ApiResult<object>(null, null);

        string? error = null;
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>(cancellationToken: ct);
            if (doc is not null && doc.TryGetValue("error", out var e))
                error = e.GetString();
        }
        catch { /* non-JSON error body */ }

        return new ApiResult<object>(null, error ?? $"HTTP {(int)response.StatusCode}");
    }
}
