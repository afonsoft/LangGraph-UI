using KnowledgeHub.Server.Ingestion;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Server;

/// <summary>
/// SPEC-20260926-sync-error-diagnostics RF-001: persisted error fields must
/// surface the innermost cause — never the generic EF wrapper text.
/// </summary>
public sealed class ExceptionDigestTests
{
    [Fact]
    public void Describe_UnwrapsToBaseException()
    {
        var inner = new InvalidOperationException("UNIQUE constraint failed: KgAliases.SourceId");
        var ex = new DbUpdateException("An error occurred while saving the entity changes. See the inner exception for details.", inner);

        var digest = ExceptionDigest.Describe(ex);

        Assert.Contains("InvalidOperationException", digest);
        Assert.Contains("UNIQUE constraint failed: KgAliases.SourceId", digest);
        Assert.DoesNotContain("saving the entity changes", digest);
    }

    [Fact]
    public void Describe_NoInner_KeepsTypeAndMessage()
    {
        var ex = new HttpRequestException("connection refused");

        var digest = ExceptionDigest.Describe(ex);

        Assert.Equal("HttpRequestException: connection refused", digest);
    }

    [Fact]
    public void Describe_StripsNewlines()
    {
        var ex = new Exception("line one\nline two\r\nline three");

        var digest = ExceptionDigest.Describe(ex);

        Assert.DoesNotContain('\n', digest);
        Assert.DoesNotContain('\r', digest);
    }

    [Fact]
    public void Describe_TruncatesLongMessages()
    {
        var ex = new Exception(new string('x', 1000));

        var digest = ExceptionDigest.Describe(ex);

        Assert.True(digest.Length <= 500);
    }
}
