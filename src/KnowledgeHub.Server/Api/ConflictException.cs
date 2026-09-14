namespace KnowledgeHub.Server.Api;

/// <summary>Domain conflict (e.g. double-resolved approval) → HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
