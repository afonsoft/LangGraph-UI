using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Connectors;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Tests.Unit.Ingestion;

// Covers SPEC-20260919-notion-connector T1/RF-007: incremental contract —
// RawDocument.Fingerprint + IIncrementalSourceConnector seam.
public class IncrementalContractTests
{
    [Fact]
    public void RawDocument_Fingerprint_IsOptionalAndCarried()
    {
        var without = new RawDocument("uri", "title", "text");
        var with = new RawDocument("uri", "title", "text", "notion:2026-09-19T00:00:00Z");

        Assert.Null(without.Fingerprint);
        Assert.Equal("notion:2026-09-19T00:00:00Z", with.Fingerprint);
    }

    [Fact]
    public async Task IncrementalConnector_ReceivesExistingFingerprintMap()
    {
        var connector = new StubIncrementalConnector();
        var source = new KnowledgeSource { Name = "n", SourceType = SourceType.Notion };
        var existing = new Dictionary<string, string> { ["notion://page/a"] = "notion:t1" };

        await connector.FetchAsync(source, existing, CancellationToken.None);

        Assert.Same(existing, connector.ReceivedMap);
    }

    private sealed class StubIncrementalConnector : IIncrementalSourceConnector
    {
        public IReadOnlyDictionary<string, string>? ReceivedMap { get; private set; }
        public SourceType Type => SourceType.Notion;

        public Task<FetchResult> FetchAsync(KnowledgeSource source, CancellationToken cancellationToken) =>
            FetchAsync(source, new Dictionary<string, string>(), cancellationToken);

        public Task<FetchResult> FetchAsync(
            KnowledgeSource source,
            IReadOnlyDictionary<string, string> existingFingerprints,
            CancellationToken cancellationToken)
        {
            ReceivedMap = existingFingerprints;
            return Task.FromResult(new FetchResult([], []));
        }
    }
}
