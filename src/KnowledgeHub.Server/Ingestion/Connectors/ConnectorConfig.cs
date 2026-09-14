using System.Text.Json;
using KnowledgeHub.Server.Domain.Entities;

namespace KnowledgeHub.Server.Ingestion.Connectors;

/// <summary>Thin typed accessor over <see cref="KnowledgeSource.ConfigurationJson"/>.</summary>
public sealed class ConnectorConfig
{
    private readonly JsonElement _root;

    private ConnectorConfig(JsonElement root) => _root = root;

    public static ConnectorConfig Parse(string? configurationJson)
    {
        if (string.IsNullOrEmpty(configurationJson))
            return new ConnectorConfig(default);
        try
        {
            return new ConnectorConfig(JsonDocument.Parse(configurationJson).RootElement.Clone());
        }
        catch (JsonException)
        {
            return new ConnectorConfig(default);
        }
    }

    public string? String(string key) =>
        _root.ValueKind == JsonValueKind.Object
        && _root.TryGetProperty(key, out var p)
        && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public int Int(string key, int fallback, int min, int max) =>
        _root.ValueKind == JsonValueKind.Object
        && _root.TryGetProperty(key, out var p)
        && p.ValueKind == JsonValueKind.Number
        && p.TryGetInt32(out var v)
            ? Math.Clamp(v, min, max)
            : fallback;

    public bool Bool(string key, bool fallback = false) =>
        _root.ValueKind == JsonValueKind.Object
        && _root.TryGetProperty(key, out var p)
        && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : fallback;
}
