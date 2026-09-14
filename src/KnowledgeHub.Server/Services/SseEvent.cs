namespace KnowledgeHub.Server.Services;

/// <summary>One typed SSE event (event: Type / data: Data) for the streaming endpoints.</summary>
public sealed record SseEvent(string Type, object Data);
