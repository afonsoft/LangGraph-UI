using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Singleton backing the /api/settings/chat endpoints and the runtime
/// <see cref="IChatClient"/> resolution (SPEC-20260916-settings-chat-config
/// RF-002/RF-003). Effective-config snapshot is loaded lazily and cached;
/// <see cref="Invalidate"/> forces a reload on next access so settings edits
/// take effect without restart.
/// </summary>
public sealed class ChatSettingsService(
    IOptions<ChatProviderOptions> envOptions,
    IIntegrationSecretStore secrets,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    ILogger<ChatSettingsService> logger,
    Func<HttpClient>? probeFactory = null) : IChatSettingsService
{
    /// <summary>Test-probe timeout — deliberately shorter than chat requests.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private volatile Snapshot? _snapshot;

    private sealed record Snapshot(ChatProviderOptions Options, IChatClient? Client);

    public ChatProviderOptions GetEffectiveOptions() => Current().Options;

    public IChatClient? GetClient() => Current().Client;

    public void Invalidate()
    {
        lock (_gate)
            _snapshot = null;
    }

    private Snapshot Current()
    {
        var snap = _snapshot;
        if (snap is not null)
            return snap;
        lock (_gate)
        {
            snap ??= LoadSnapshotAsync().GetAwaiter().GetResult();
            _snapshot = snap;
            return snap;
        }
    }

    private async Task<Snapshot> LoadSnapshotAsync()
    {
        var env = envOptions.Value;
        var row = await FindRowAsync(CancellationToken.None);

        ChatProviderOptions options;
        if (row is not null)
        {
            options = new ChatProviderOptions
            {
                Provider = "openai",
                Endpoint = row.Endpoint,
                Model = row.Model,
                ApiKey = await secrets.GetAsync(IntegrationProviders.Chat) ?? env.ApiKey,
                Temperature = env.Temperature,
                MaxTokens = env.MaxTokens,
                TimeoutSeconds = env.TimeoutSeconds
            };
        }
        else
        {
            options = env;
        }

        IChatClient? client = null;
        try
        {
            client = ChatClientFactory.Create(options, httpFactory);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "chat client build failed for provider {Provider} — treating as unconfigured", options.Provider);
        }
        return new Snapshot(options, client);
    }

    public async Task<ChatSettingsDto> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var env = envOptions.Value;
        var envConfigured = !string.IsNullOrWhiteSpace(env.Provider)
            && !env.Provider.Equals("none", StringComparison.OrdinalIgnoreCase);
        var row = await FindRowAsync(cancellationToken);

        var info = await secrets.GetInfoAsync(IntegrationProviders.Chat, cancellationToken);
        var (hasKey, hint, keySource) = info is not null
            ? (true, $"••••{info.KeyHint}", "store")
            : !string.IsNullOrWhiteSpace(env.ApiKey)
                ? (true, $"••••{(env.ApiKey.Length >= 4 ? env.ApiKey[^4..] : env.ApiKey)}", "env")
                : (false, (string?)null, "none");

        if (row is not null)
            return new ChatSettingsDto
            {
                Provider = "openai",
                Endpoint = row.Endpoint,
                Model = row.Model,
                HasApiKey = hasKey,
                ApiKeyHint = hint,
                ApiKeySource = keySource,
                Source = "store",
                EnvConfigured = envConfigured,
                UpdatedAt = row.UpdatedAt
            };

        if (envConfigured)
            return new ChatSettingsDto
            {
                Provider = env.Provider,
                Endpoint = env.Endpoint,
                Model = env.Model,
                HasApiKey = hasKey,
                ApiKeyHint = hint,
                ApiKeySource = keySource,
                Source = "env",
                EnvConfigured = true
            };

        return new ChatSettingsDto
        {
            Provider = "none",
            HasApiKey = hasKey,
            ApiKeyHint = hint,
            ApiKeySource = keySource,
            Source = "none",
            EnvConfigured = false
        };
    }

    public async Task SaveAsync(string endpoint, string model, string? apiKey, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        var row = await db.ChatSettings.SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new ChatSettings { Id = 1, Endpoint = endpoint, Model = model };
            db.ChatSettings.Add(row);
        }
        else
        {
            row.Endpoint = endpoint;
            row.Model = model;
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(apiKey))
            await secrets.SetAsync(IntegrationProviders.Chat, apiKey.Trim(), cancellationToken);

        Invalidate();
        logger.LogInformation("chat settings saved (endpoint {Endpoint}, model {Model}, key {KeyAction})",
            endpoint, model, string.IsNullOrWhiteSpace(apiKey) ? "kept" : "updated");
    }

    public async Task RemoveKeyAsync(CancellationToken cancellationToken = default)
    {
        await secrets.RemoveAsync(IntegrationProviders.Chat, cancellationToken);
        Invalidate();
        logger.LogInformation("chat API key removed from store");
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        await db.ChatSettings.ExecuteDeleteAsync(cancellationToken);
        await secrets.RemoveAsync(IntegrationProviders.Chat, cancellationToken);
        Invalidate();
        logger.LogInformation("chat settings cleared — falling back to env/config");
    }

    public async Task<TestChatConnectionResponse> TestAsync(
        TestChatConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var effective = Current().Options;
        var endpoint = !string.IsNullOrWhiteSpace(request.Endpoint) ? request.Endpoint.Trim() : effective.Endpoint;
        var model = !string.IsNullOrWhiteSpace(request.Model) ? request.Model.Trim() : effective.Model;
        var apiKey = !string.IsNullOrWhiteSpace(request.ApiKey) ? request.ApiKey.Trim() : effective.ApiKey;

        if (string.IsNullOrWhiteSpace(endpoint))
            return new TestChatConnectionResponse { Ok = false, LatencyMs = 0, Detail = "endpoint is required" };

        var http = probeFactory?.Invoke() ?? httpFactory.CreateClient("chat");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.TrimEnd('/')}/v1/models");
            if (!string.IsNullOrEmpty(apiKey))
                probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await http.SendAsync(probe, timeout.Token);
            var latency = sw.ElapsedMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("chat connection test failed: HTTP {Status} in {LatencyMs} ms",
                    (int)response.StatusCode, latency);
                return new TestChatConnectionResponse
                {
                    Ok = false,
                    LatencyMs = latency,
                    Detail = $"HTTP {(int)response.StatusCode}"
                };
            }

            var modelListed = await CheckModelListedAsync(response, model, timeout.Token);
            logger.LogInformation("chat connection test ok in {LatencyMs} ms (modelListed={ModelListed})",
                latency, modelListed);
            return new TestChatConnectionResponse { Ok = true, LatencyMs = latency, ModelListed = modelListed };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TestChatConnectionResponse { Ok = false, LatencyMs = sw.ElapsedMilliseconds, Detail = "timeout" };
        }
        catch (HttpRequestException)
        {
            return new TestChatConnectionResponse { Ok = false, LatencyMs = sw.ElapsedMilliseconds, Detail = "connection failed" };
        }
    }

    /// <summary>True/false when the probe body is an OpenAI-style model list and a
    /// model was supplied; null when unparseable or no model to check.</summary>
    private static async Task<bool?> CheckModelListedAsync(
        HttpResponseMessage response, string? model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            return data.EnumerateArray().Any(m =>
                m.TryGetProperty("id", out var id) && id.GetString() == model);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Domain.Entities.ChatSettings?> FindRowAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
        return await db.ChatSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }
}
