namespace KnowledgeHub.Server.Chat;

/// <summary>Provider-side failure (HTTP error, timeout, malformed payload) — maps to HTTP 502 / MCP isError.</summary>
public sealed class ChatProviderException(string message) : Exception(message);
