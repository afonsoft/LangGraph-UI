using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Server.Mcp.Upstream;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeHub.Server.Services;

/// <summary>CRUD + validation + secret redaction for knowledge sources (SPEC-02 RF-001/RF-002).</summary>
public sealed class KnowledgeSourceService(
    KnowledgeHubDbContext db,
    IToolCatalogChangeNotifier catalogNotifier,
    IIntegrationSecretStore secrets) : IKnowledgeSourceService
{
    /// <summary>Config keys that must never be echoed back to API consumers.</summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
        { "connectionString", "apiKey", "key", "headers", "token", "password", "secret" };

    /// <summary>Required configuration keys per connector type (SPEC-02 §Scope).</summary>
    private static readonly Dictionary<SourceType, string[]> RequiredKeys = new()
    {
        [SourceType.ObsidianVault] = ["path"],
        [SourceType.WebPage] = ["url"],
        [SourceType.RestApi] = ["endpoint"],
        [SourceType.SqlDatabase] = ["connectionString", "query"],
        [SourceType.DocumentFile] = ["path"],
        [SourceType.McpProxy] = ["endpoint"]
    };

    public async Task<IReadOnlyList<KnowledgeSourceDto>> ListAsync(SourceType? type, bool? active, CancellationToken ct = default)
    {
        var query = db.Sources.AsNoTracking();
        if (type is not null)
            query = query.Where(s => s.SourceType == type);
        if (active is not null)
            query = query.Where(s => s.IsActive == active);
        var sources = await query.OrderBy(s => s.Name).ToListAsync(ct);
        return sources.Select(ToDto).ToList();
    }

    public async Task<KnowledgeSourceDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Sources.AsNoTracking().Where(s => s.Id == id).Select(s => ToDto(s)).FirstOrDefaultAsync(ct);

    public async Task<ServiceResult<KnowledgeSourceDto>> CreateAsync(CreateKnowledgeSourceRequest request, CancellationToken ct = default)
    {
        var validation = Validate(request.Name, request.Type, request.Configuration);
        if (validation is not null)
            return ServiceResult<KnowledgeSourceDto>.Fail(400, validation);

        if (await db.Sources.AnyAsync(s => s.Name == request.Name, ct))
            return ServiceResult<KnowledgeSourceDto>.Fail(409, $"A source named '{request.Name}' already exists");

        var source = new KnowledgeSource
        {
            Name = request.Name,
            Description = request.Description,
            SourceType = request.Type,
            ConfigurationJson = request.Configuration?.ToJsonString(),
            IsActive = request.IsActive,
            AutoSyncEnabled = request.AutoSyncEnabled,
            SyncIntervalMinutes = request.SyncIntervalMinutes
        };
        db.Sources.Add(source);
        await PersistProxySecretAsync(source, request.Configuration, ct);
        await db.SaveChangesAsync(ct);
        await NotifyCatalogChanged(ct);
        return ServiceResult<KnowledgeSourceDto>.Ok(ToDto(source));
    }

    public async Task<ServiceResult<KnowledgeSourceDto>> UpdateAsync(Guid id, UpdateKnowledgeSourceRequest request, CancellationToken ct = default)
    {
        var source = await db.Sources.FindAsync([id], ct);
        if (source is null)
            return ServiceResult<KnowledgeSourceDto>.Fail(404, "Source not found");

        var validation = Validate(request.Name, source.SourceType, request.Configuration);
        if (validation is not null)
            return ServiceResult<KnowledgeSourceDto>.Fail(400, validation);

        if (await db.Sources.AnyAsync(s => s.Name == request.Name && s.Id != id, ct))
            return ServiceResult<KnowledgeSourceDto>.Fail(409, $"A source named '{request.Name}' already exists");

        source.Name = request.Name;
        source.Description = request.Description;
        source.ConfigurationJson = request.Configuration?.ToJsonString();
        source.IsActive = request.IsActive;
        source.AutoSyncEnabled = request.AutoSyncEnabled;
        source.SyncIntervalMinutes = request.SyncIntervalMinutes;
        await PersistProxySecretAsync(source, request.Configuration, ct);
        await db.SaveChangesAsync(ct);
        await NotifyCatalogChanged(ct);
        return ServiceResult<KnowledgeSourceDto>.Ok(ToDto(source));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var source = await db.Sources.FindAsync([id], ct);
        if (source is null)
            return ServiceResult<bool>.Fail(404, "Source not found");
        db.Sources.Remove(source); // cascade removes documents + chunks
        if (source.SourceType == SourceType.McpProxy)
            await secrets.RemoveAsync(McpProxySession.SecretKey(source.Id), ct);
        await db.SaveChangesAsync(ct);
        await NotifyCatalogChanged(ct);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<KnowledgeSourceDto>> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        var source = await db.Sources.FindAsync([id], ct);
        if (source is null)
            return ServiceResult<KnowledgeSourceDto>.Fail(404, "Source not found");
        source.IsActive = active;
        await db.SaveChangesAsync(ct);
        await NotifyCatalogChanged(ct);
        return ServiceResult<KnowledgeSourceDto>.Ok(ToDto(source));
    }

    /// <summary>Moves <c>configuration.apiKey</c> to the encrypted store for
    /// McpProxy sources (SPEC-20260917-mcp-proxy-source-type RF-003): the
    /// persisted configuration keeps only a non-sensitive <c>hasKey</c> flag.
    /// An absent <c>apiKey</c> keeps the stored key; <c>"***"</c> (the redaction
    /// marker echoed by edited forms) also keeps it; empty removes it.</summary>
    private async Task PersistProxySecretAsync(KnowledgeSource source, JsonObject? configuration, CancellationToken ct)
    {
        if (source.SourceType != SourceType.McpProxy || configuration is null)
            return;

        var config = JsonNode.Parse(source.ConfigurationJson ?? "{}") as JsonObject ?? new JsonObject();
        config.Remove("apiKey");
        var secretKey = McpProxySession.SecretKey(source.Id);

        if (configuration.TryGetPropertyValue("apiKey", out var keyNode)
            && keyNode?.GetValue<string>() is { } key
            && key != "***")
        {
            if (key.Length == 0)
            {
                await secrets.RemoveAsync(secretKey, ct);
                config["hasKey"] = false;
            }
            else
            {
                await secrets.SetAsync(secretKey, key, ct);
                config["hasKey"] = true;
            }
        }
        else
        {
            config["hasKey"] = await secrets.GetAsync(secretKey, ct) is not null;
        }

        source.ConfigurationJson = config.ToJsonString();
    }

    /// <summary>Catalog mutations must never fail the REST call — notification is best-effort.</summary>
    private async Task NotifyCatalogChanged(CancellationToken ct)
    {
        try { await catalogNotifier.NotifyToolsChangedAsync(ct); }
        catch { /* connected-client notification is advisory */ }
    }

    public async Task<IReadOnlyList<KnowledgeDocumentDto>?> ListDocumentsAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.Sources.AnyAsync(s => s.Id == id, ct))
            return null;
        return await db.Documents.AsNoTracking()
            .Where(d => d.KnowledgeSourceId == id)
            .OrderBy(d => d.Title)
            .Select(d => new KnowledgeDocumentDto
            {
                Id = d.Id,
                SourceId = d.KnowledgeSourceId,
                Title = d.Title,
                UriReference = d.UriReference,
                ChunkCount = d.Chunks.Count,
                IndexedAt = d.IndexedAt
            })
            .ToListAsync(ct);
    }

    private static string? Validate(string? name, SourceType type, JsonObject? configuration)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required";
        if (!Enum.IsDefined(type))
            return $"Invalid source type '{type}'";

        if (configuration is null)
            return $"Configuration is required for {type} (expects {string.Join(", ", RequiredKeys[type])})";

        foreach (var key in RequiredKeys[type])
        {
            if (configuration[key] is null || string.IsNullOrWhiteSpace(configuration[key]?.GetValue<string>()))
                return $"Configuration key '{key}' is required for {type}";
        }

        if (type == SourceType.McpProxy)
        {
            var endpoint = configuration["endpoint"]?.GetValue<string>();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
                return "Configuration key 'endpoint' must be an absolute http(s) URI for McpProxy";
            if (configuration["transport"]?.GetValue<string>()?.ToLowerInvariant()
                    is not (null or "auto" or "http" or "sse"))
                return "Configuration key 'transport' must be auto|http|sse for McpProxy";
        }
        return null;
    }

    private static KnowledgeSourceDto ToDto(KnowledgeSource s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        Type = s.SourceType,
        Configuration = Redact(s.ConfigurationJson),
        IsActive = s.IsActive,
        AutoSyncEnabled = s.AutoSyncEnabled,
        SyncIntervalMinutes = s.SyncIntervalMinutes,
        CreatedAt = s.CreatedAt,
        LastSyncAt = s.LastSyncAt,
        LastSyncStatus = s.LastSyncStatus,
        LastError = s.LastError
    };

    /// <summary>Strip sensitive keys so API responses never echo secrets (SPEC-02 §Guardrails).</summary>
    public static JsonObject? Redact(string? configurationJson)
    {
        if (string.IsNullOrEmpty(configurationJson))
            return null;
        try
        {
            var node = JsonNode.Parse(configurationJson) as JsonObject;
            if (node is null)
                return null;
            var clone = (JsonObject)node.DeepClone();
            foreach (var key in SensitiveKeys)
            {
                if (clone.ContainsKey(key))
                    clone[key] = "***";
            }
            return clone;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
