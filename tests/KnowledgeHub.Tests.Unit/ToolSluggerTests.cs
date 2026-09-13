using KnowledgeHub.Server.Mcp;

namespace KnowledgeHub.Tests.Unit;

public class ToolSluggerTests
{
    [Theory]
    [InlineData("Meu Vault", "meu_vault")]
    [InlineData("Meu Vault!", "meu_vault")]
    [InlineData("Notas & Docs 2024", "notas_docs_2024")]
    [InlineData("UPPER case", "upper_case")]
    [InlineData("!!!", "source")]
    [InlineData("", "source")]
    [InlineData("  -_  spaced  _- ", "spaced")]
    public void Slugify_NormalizesToLowerUnderscore(string name, string expected)
    {
        Assert.Equal(expected, ToolSlugger.Slugify(name));
    }

    [Fact]
    public void Assign_Collisions_GetDeterministicSuffixes()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var slugs = ToolSlugger.Assign(new[]
        {
            (ids[0], "Meu Vault!"),
            (ids[1], "Meu Vault"),
            (ids[2], "Meu Vault!!")
        });

        Assert.Equal("meu_vault", slugs[ids[0]]);
        Assert.Equal("meu_vault_2", slugs[ids[1]]);
        Assert.Equal("meu_vault_3", slugs[ids[2]]);
    }

    [Fact]
    public void Assign_DistinctNames_KeepDistinctSlugs()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var slugs = ToolSlugger.Assign(new[]
        {
            (ids[0], "Alpha"),
            (ids[1], "Beta")
        });

        Assert.Equal("alpha", slugs[ids[0]]);
        Assert.Equal("beta", slugs[ids[1]]);
    }
}
