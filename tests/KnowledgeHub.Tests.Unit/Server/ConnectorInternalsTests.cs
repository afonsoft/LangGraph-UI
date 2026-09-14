using System.Net;
using KnowledgeHub.Server.Ingestion.Connectors;

namespace KnowledgeHub.Tests.Unit.Server;

// Covers SPEC-20260914-webpage-docfile-connectors: HTML extraction, glob, SSRF guard, robots.
public class ConnectorInternalsTests
{
    [Fact]
    public void Extract_StripsBoilerplate_KeepsHeadings()
    {
        var html = """
            <html><head><title>T</title><style>x{}</style></head>
            <body><nav>menu</nav><article><h1>Main Title</h1><p>Real content here.</p>
            <script>bad()</script><ul><li>item one</li></ul></article><footer>foot</footer></body></html>
            """;

        var text = HtmlTextExtractor.Extract(html);

        Assert.Contains("# Main Title", text);
        Assert.Contains("Real content here.", text);
        Assert.Contains("- item one", text);
        Assert.DoesNotContain("menu", text);
        Assert.DoesNotContain("foot", text);
        Assert.DoesNotContain("bad()", text);
    }

    [Fact]
    public void Extract_ExcludeSelector_RemovesMatchingSubtree()
    {
        var html = "<html><body><p>keep</p><div class=\"ad\">buy stuff</div></body></html>";
        var text = HtmlTextExtractor.Extract(html, excludeSelector: ".ad");
        Assert.Contains("keep", text);
        Assert.DoesNotContain("buy stuff", text);
    }

    [Fact]
    public void ExtractLinks_SameOriginOnly()
    {
        var html = """
            <html><body>
            <a href="/local">a</a><a href="https://other.com/x">b</a><a href="#frag">c</a>
            </body></html>
            """;
        // #frag resolves to the same path — the crawler's visited-set dedupes it.
        var links = HtmlTextExtractor.ExtractLinks(html, new Uri("https://example.com/page"));
        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/local", links[0]);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.5.5")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("::1")]
    public void SsrfGuard_PrivateAddresses_Blocked(string ip)
    {
        Assert.True(WebPageConnector.IsPrivate(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    public void SsrfGuard_PublicAddresses_Allowed(string ip)
    {
        Assert.False(WebPageConnector.IsPrivate(IPAddress.Parse(ip)));
    }

    [Fact]
    public void Robots_DisallowPrefix_BlocksPath()
    {
        var policy = WebPageConnector.RobotsPolicy.Parse("""
            User-agent: *
            Disallow: /private
            Disallow: /admin/panel
            """);
        Assert.False(policy.Allows(new Uri("https://example.com/private/page")));
        Assert.True(policy.Allows(new Uri("https://example.com/public")));
    }

    [Theory]
    [InlineData("**/*", "a/b/c.txt", true)]
    [InlineData("*.md", "note.md", true)]
    [InlineData("*.md", "sub/note.md", false)]
    [InlineData("**/*.pdf", "sub/deep/doc.pdf", true)]
    [InlineData("**/*.pdf", "doc.txt", false)]
    public void GlobMatcher_MatchesExpected(string glob, string path, bool expected)
    {
        Assert.Equal(expected, DocumentFileConnector.GlobMatcher.Compile(glob)(path));
    }
}
