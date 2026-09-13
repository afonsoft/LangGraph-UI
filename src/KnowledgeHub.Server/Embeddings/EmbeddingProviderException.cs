namespace KnowledgeHub.Server.Embeddings;

/// <summary>Sanitized embedding failure — carries status only, never credentials or payloads.</summary>
public sealed class EmbeddingProviderException(string message) : Exception(message);
