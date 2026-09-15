using System.Net.Http.Json;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Client.Services;

/// <summary>Typed client for /api/auth + /api/apikeys (SPEC-20260914-auth-login RF-011/RF-012).</summary>
public sealed class AuthApiClient(HttpClient http)
{
    public async Task<ApiResult<LoginResponse>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password), ct);
        return await ReadAsync<LoginResponse>(response, ct);
    }

    public async Task<MeResponse?> MeAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("api/auth/me", ct);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: ct)
            : null;
    }

    public async Task<ApiResult<object>> ChangePasswordAsync(string current, string next, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/auth/change-password",
            new ChangePasswordRequest(current, next), ct);
        return await ReadAsync<object>(response, ct);
    }

    public async Task<ApiResult<object>> LogoutAsync(CancellationToken ct = default)
    {
        var response = await http.PostAsync("api/auth/logout", null, ct);
        return await ReadAsync<object>(response, ct);
    }

    public Task<List<ApiKeyDto>?> ListKeysAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<ApiKeyDto>>("api/apikeys", ct);

    public async Task<ApiResult<ApiKeyCreatedDto>> CreateKeyAsync(string name, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/apikeys", new CreateApiKeyRequest(name), ct);
        return await ReadAsync<ApiKeyCreatedDto>(response, ct);
    }

    public async Task<ApiResult<object>> RevokeKeyAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/apikeys/{id}", ct);
        return await ReadAsync<object>(response, ct);
    }

    // SPEC-20260915-apikey-usage-audit RF-006: per-key usage summary + audit trail.
    public Task<ApiKeyUsageDto?> GetKeyUsageAsync(Guid id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<ApiKeyUsageDto>($"api/apikeys/{id}/usage", ct);

    private static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = response.StatusCode == System.Net.HttpStatusCode.NoContent
                ? default
                : await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
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
