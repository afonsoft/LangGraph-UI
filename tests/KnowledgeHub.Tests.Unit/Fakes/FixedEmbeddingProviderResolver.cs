using KnowledgeHub.Server.Embeddings;

namespace KnowledgeHub.Tests.Unit.Fakes;

/// <summary>Fixed resolver for tests — wraps a stub provider and reports a
/// constant fingerprint so embedding cache keys stay stable per test.</summary>
public sealed class FixedEmbeddingProviderResolver(IEmbeddingProvider provider, string fingerprint = "testfp00")
    : IEmbeddingProviderResolver
{
    public IEmbeddingProvider Current => provider;
    public string Fingerprint => fingerprint;
}
