using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Client.Services;

/// <summary>Typed client for /api/sources (SPEC-05 RF-001/RF-002).</summary>
public sealed class SourceApiClient(HttpClient http)
{
    public Task<List<KnowledgeSourceDto>?> ListAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<KnowledgeSourceDto>>("api/sources", ct);

    public async Task<ApiResult<KnowledgeSourceDto>> CreateAsync(CreateKnowledgeSourceRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("api/sources", request, ct);
        return await ReadResultAsync<KnowledgeSourceDto>(response, ct);
    }

    public async Task<ApiResult<KnowledgeSourceDto>> UpdateAsync(Guid id, UpdateKnowledgeSourceRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"api/sources/{id}", request, ct);
        return await ReadResultAsync<KnowledgeSourceDto>(response, ct);
    }

    public async Task<ApiResult<KnowledgeSourceDto>> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/sources/{id}/{(active ? "activate" : "deactivate")}", null, ct);
        return await ReadResultAsync<KnowledgeSourceDto>(response, ct);
    }

    public async Task<ApiResult<SyncResultDto>> SyncAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"api/sources/{id}/sync", null, ct);
        return await ReadResultAsync<SyncResultDto>(response, ct);
    }

    public async Task<ApiResult<object>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"api/sources/{id}", ct);
        return await ReadResultAsync<object>(response, ct);
    }

    public Task<List<KnowledgeDocumentDto>?> DocumentsAsync(Guid id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<KnowledgeDocumentDto>>($"api/sources/{id}/documents", ct);

    private static async Task<ApiResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return new ApiResult<T>(value, null);
        }

        var (error, detail) = await TryReadErrorAsync(response, ct);
        return new ApiResult<T>(default, error ?? $"HTTP {(int)response.StatusCode}", detail);
    }

    private static async Task<(string? Error, string Detail)> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
                detail += $"\n{body}";
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
            var error = dict is not null &&
                (dict.TryGetValue("error", out var e) || dict.TryGetValue("detail", out e))
                    ? e : null;
            return (error, detail);
        }
        catch
        {
            return (response.StatusCode == HttpStatusCode.Conflict ? "Conflict" : null, detail);
        }
    }
}

public sealed record ApiResult<T>(T? Value, string? Error, string? Detail = null)
{
    public bool IsSuccess => Error is null;
}
