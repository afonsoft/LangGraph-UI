using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>Conversation thread CRUD + append (SPEC-20260914-conversation-threads RF-001).</summary>
public interface IConversationService
{
    Task<IReadOnlyList<ThreadDto>> ListAsync(CancellationToken ct = default);
    Task<ThreadDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ThreadDto> CreateAsync(string? title, CancellationToken ct = default);
    Task<ThreadDto> RenameAsync(Guid id, string title, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
