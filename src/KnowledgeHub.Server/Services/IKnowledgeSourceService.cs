using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Services;

/// <summary>CRUD + lifecycle operations for knowledge sources (SPEC-02 RF-002).</summary>
public interface IKnowledgeSourceService
{
    Task<IReadOnlyList<KnowledgeSourceDto>> ListAsync(SourceType? type, bool? active, CancellationToken ct = default);
    Task<KnowledgeSourceDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResult<KnowledgeSourceDto>> CreateAsync(CreateKnowledgeSourceRequest request, CancellationToken ct = default);
    Task<ServiceResult<KnowledgeSourceDto>> UpdateAsync(Guid id, UpdateKnowledgeSourceRequest request, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ServiceResult<KnowledgeSourceDto>> SetActiveAsync(Guid id, bool active, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeDocumentDto>?> ListDocumentsAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Service outcome with HTTP-mappable error info.</summary>
public sealed record ServiceResult<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    /// <summary>400 | 404 | 409 — null on success.</summary>
    public int? ErrorStatus { get; init; }

    public static ServiceResult<T> Ok(T value) => new() { Value = value };
    public static ServiceResult<T> Fail(int status, string error) => new() { Error = error, ErrorStatus = status };
}
