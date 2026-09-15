using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Tests.Unit.Server;

// SPEC-20260915-sources-edit-dialog RF-002: per-source readOnly write guard.
public class ObsidianNoteWriterTests
{
    private static KnowledgeSource Source(string? config) => new()
    {
        Name = "v",
        SourceType = SourceType.ObsidianVault,
        ConfigurationJson = config
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"readOnly": false}""")]
    [InlineData("""{"readOnly": "true"}""")]
    [InlineData("not json")]
    public void IsReadOnly_AbsentOrMalformed_ReturnsFalse(string? config) =>
        Assert.False(ObsidianNoteWriter.IsReadOnly(Source(config)));

    [Fact]
    public void IsReadOnly_TrueFlag_ReturnsTrue() =>
        Assert.True(ObsidianNoteWriter.IsReadOnly(Source("""{"path": "/vault", "readOnly": true}""")));
}
