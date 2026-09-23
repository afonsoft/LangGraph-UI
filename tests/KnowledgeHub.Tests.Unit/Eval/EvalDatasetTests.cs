using KnowledgeHub.Server.Eval;
using Xunit;

namespace KnowledgeHub.Tests.Unit.Eval;

/// <summary>SPEC-20260923-eval-harness RF-001 — dataset parsing/validation.</summary>
public sealed class EvalDatasetTests
{
    [Fact]
    public void Parse_ValidDataset()
    {
        var (cases, errors) = EvalDataset.Parse("""
            [{"id":"a","question":"q?","expectedUris":["u1"]},
             {"id":"b","question":"q2?","expectedTextMarkers":["x"],"tags":["t"]}]
            """);
        Assert.Empty(errors);
        Assert.Equal(2, cases.Count);
        Assert.Equal("a", cases[0].Id);
    }

    [Fact]
    public void Parse_MalformedJson()
    {
        var (cases, errors) = EvalDataset.Parse("not json");
        Assert.Empty(cases);
        Assert.Single(errors);
    }

    [Fact]
    public void Parse_EmptyArray_Rejected()
    {
        var (_, errors) = EvalDataset.Parse("[]");
        Assert.Single(errors);
    }

    [Fact]
    public void Parse_PerCaseErrors()
    {
        var (_, errors) = EvalDataset.Parse("""
            [{"id":"a","question":""},
             {"id":"a","question":"q","expectedUris":["u"]},
             {"id":"c","question":"q","mode":"bogus"}]
            """);
        // case[0]: empty question + missing expected; case[1]: duplicate id;
        // case[2]: bad mode + missing expected
        Assert.Equal(5, errors.Count);
    }
}
