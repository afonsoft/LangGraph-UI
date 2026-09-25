using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Settings;

/// <summary>
/// Effective embedding/indexing configuration — store overrides env/config,
/// per SPEC-20260926-settings-ux-embeddings RF-004.
/// </summary>
public interface IEmbeddingSettingsService
{
    /// <summary>Effective <see cref="EmbeddingOptions"/> (store row over env).
    /// Resolved per call — picks up edits immediately via the cached snapshot.</summary>
    EmbeddingOptions GetEffectiveOptions();

    /// <summary>Effective chunking knobs (store row over env
    /// <c>Ingestion:MaxTokens</c>/<c>Ingestion:OverlapTokens</c>).</summary>
    (int MaxTokens, int OverlapTokens) GetChunking();

    /// <summary>Masked effective state for GET /api/settings/embeddings.</summary>
    Task<EmbeddingSettingsDto> DescribeAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts the single-row override; blank key keeps the stored one.</summary>
    Task SaveAsync(SaveEmbeddingSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored API key only (env key wins again).</summary>
    Task RemoveKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the row + stored key — env/config is fully restored.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Discards the cached snapshot; next access re-reads the store.</summary>
    void Invalidate();
}
