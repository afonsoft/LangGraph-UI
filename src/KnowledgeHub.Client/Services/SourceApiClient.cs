using System.Net;
using System.Net.Http.Json;
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

        var error = await TryReadErrorAsync(response, ct);
        return new ApiResult<T>(default, error ?? $"HTTP {(int)response.StatusCode}");
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken: ct);
            return body is not null && body.TryGetValue("error", out var e) ? e : null;
        }
        catch
        {
            return response.StatusCode == HttpStatusCode.Conflict ? "Conflict" : null;
        }
    }
}

public sealed record ApiResult<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;
}
