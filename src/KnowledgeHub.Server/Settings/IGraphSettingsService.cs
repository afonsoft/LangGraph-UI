using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Settings;

/// <summary>Effective graph settings snapshot — store row over env/config.</summary>
public sealed record GraphSettingsSnapshot(
    bool Enabled, int MaxChunksPerSync, int MaxChunkChars, int MaxResults, string Source);

/// <summary>
/// Runtime-editable GraphRAG settings (SPEC-20260923-graph-settings-ui
/// RF-002). Read path is a cached snapshot; edits call <see cref="Invalidate"/>
/// so consumers see new values without restart.
/// </summary>
public interface IGraphSettingsService
{
    /// <summary>Effective snapshot — store row, else Graph:* config, else defaults.</summary>
    GraphSettingsSnapshot GetEffective();
    /// <summary>UI-facing description of the effective state.</summary>
    Task<GraphSettingsDto> DescribeAsync(CancellationToken cancellationToken = default);
    /// <summary>Upserts the single-row override, invalidates the snapshot and
    /// notifies the tool catalog (Enabled gates tool visibility).</summary>
    Task SaveAsync(SaveGraphSettingsRequest request, CancellationToken cancellationToken = default);
    /// <summary>Deletes the override — Graph:* config/defaults apply again.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
    /// <summary>Drops the cached snapshot; next access reloads from the store.</summary>
    void Invalidate();
}
