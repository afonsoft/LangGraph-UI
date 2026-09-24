using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Tests.Unit.Search;

/// <summary>SPEC-20260924-graph-tool-discovery: fixed-snapshot graph settings stub.</summary>
public sealed class FakeGraphSettings(bool enabled = true) : IGraphSettingsService
{
    public GraphSettingsSnapshot GetEffective() =>
        new(enabled, 200, 2000, 200, enabled ? "store" : "env");

    public Task<GraphSettingsDto> DescribeAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task SaveAsync(SaveGraphSettingsRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public void Invalidate() { }

    public static readonly FakeGraphSettings Enabled = new(true);
    public static readonly FakeGraphSettings Disabled = new(false);
}
