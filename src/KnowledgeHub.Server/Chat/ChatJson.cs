using System.Text.Json;

namespace KnowledgeHub.Server.Chat;

/// <summary>Defaults e helpers de serialização compartilhados pelos chat clients
/// (OpenAI-compatible e Ollama): gateways restritos rejeitam campos nulos, então
/// requests sempre saem com valores concretos.</summary>
internal static class ChatJson
{
    /// <summary>Temperature default quando nada foi configurado (OpenAI spec: 1.0).</summary>
    public const double DefaultTemperature = 1.0;

    /// <summary>Teto de tokens default quando nada foi configurado.</summary>
    public const int DefaultMaxTokens = 4096;

    /// <summary>Schema JSON default para tools sem parameters declarados.</summary>
    public static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

    /// <summary>Retorna o schema informado ou <see cref="EmptyObjectSchema"/> quando indefinido/nulo.</summary>
    public static JsonElement SchemaOrDefault(JsonElement schema) =>
        schema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? EmptyObjectSchema : schema;
}
