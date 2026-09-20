using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Ingestion.Connectors;
using KnowledgeHub.Server.Settings;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgeHub.Tests.Unit.Ingestion;

// Covers SPEC-20260919-notion-connector RF-003/RF-005/RF-007: discovery via
// /search, root traversal, fingerprint skip, database rows, per-item warnings,
// maxPages bound and the token resolution contract.
public class NotionConnectorTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Page(string id, string title, string edited = "2026-09-19T10:00:00Z") => $$$"""
        {"object":"page","id":"{{{id}}}","last_edited_time":"{{{edited}}}",
         "properties":{"title":{"type":"title","title":[{"plain_text":"{{{title}}}"}]}} }
        """;

    private static string ParagraphBlock(string id, string text) => $$$"""
        {"id":"{{{id}}}","type":"paragraph","has_children":false,
         "paragraph":{"rich_text":[{"plain_text":"{{{text}}}"}]}}
        """;

    private static string Results(params string[] items) =>
        $$$"""{"results":[{{{string.Join(",", items)}}}],"has_more":false,"next_cursor":null}""";

    private sealed class FakeApi
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
        public List<(string Method, string Path)> Calls { get; } = [];

        public FakeApi OnGet(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[("GET " + path)] = _ => new HttpResponseMessage(status) { Content = Json(body) };
            return this;
        }

        public FakeApi OnPost(string path, string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _routes[("POST " + path)] = _ => new HttpResponseMessage(status) { Content = Json(body) };
            return this;
        }

        public FakeApi OnGetSequence(string path, params (HttpStatusCode Status, string Body)[] responses)
        {
            var i = 0;
            _routes[("GET " + path)] = _ =>
            {
                var (s, b) = responses[Math.Min(i++, responses.Length - 1)];
                return new HttpResponseMessage(s) { Content = Json(b) };
            };
            return this;
        }

        public int Count(string pathPrefix) => Calls.Count(c => c.Path.StartsWith(pathPrefix));

        public HttpResponseMessage Route(HttpRequestMessage request)
        {
            var key = request.Method.Method + " " + request.RequestUri!.AbsolutePath;
            Calls.Add((request.Method.Method, request.RequestUri.AbsolutePath));
            return _routes.TryGetValue(key, out var handler)
                ? handler(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = Json("""{"object":"error","code":"object_not_found","message":"not found"}""")
                };
        }

        private static StringContent Json(string body) =>
            new(body, Encoding.UTF8, "application/json");
    }

    private sealed class RouterHandler(FakeApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(api.Route(request));
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://api.notion.test") };
    }

    private sealed class FakeSecrets(string? token = "ntn_test") : IIntegrationSecretStore
    {
        public Task<string?> GetAsync(string provider, CancellationToken ct = default) => Task.FromResult(token);
        public Task<IntegrationSecretInfo?> GetInfoAsync(string provider, CancellationToken ct = default) =>
            Task.FromResult<IntegrationSecretInfo?>(null);
        public Task SetAsync(string provider, string secret, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> RemoveAsync(string provider, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static NotionConnector Sut(FakeApi api, string? token = "ntn_test") =>
        new(new FakeFactory(new RouterHandler(api)), new FakeSecrets(token),
            NullLogger<NotionConnector>.Instance);

    private static KnowledgeSource Source(object configuration) => new()
    {
        Id = Guid.NewGuid(),
        Name = "notion-src",
        SourceType = SourceType.Notion,
        ConfigurationJson = JsonSerializer.Serialize(configuration)
    };

    private static JsonObject Config(object o) => JsonSerializer.SerializeToNode(o)!.AsObject();

    [Fact]
    public async Task SearchDiscovery_PagesBecomeDocuments()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "Alpha"), Page("p2", "Beta")))
            .OnGet("/v1/blocks/p1/children", Results(ParagraphBlock("b1", "alpha body")))
            .OnGet("/v1/blocks/p2/children", Results(ParagraphBlock("b2", "beta body")));

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), CancellationToken.None);

        Assert.Equal(2, result.Documents.Count);
        var first = result.Documents[0];
        Assert.Equal("notion://page/p1", first.UriReference);
        Assert.Equal("Alpha", first.Title);
        Assert.Contains("alpha body", first.TextContent);
        Assert.Equal("notion:2026-09-19T10:00:00Z", first.Fingerprint);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task FingerprintMatch_SkipsBlockFetch_EmitsEmptyContent()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "Alpha")));

        var existing = new Dictionary<string, string>
        {
            ["notion://page/p1"] = "notion:2026-09-19T10:00:00Z"
        };

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), existing, CancellationToken.None);

        var doc = Assert.Single(result.Documents);
        Assert.Equal("", doc.TextContent);
        Assert.Equal("notion:2026-09-19T10:00:00Z", doc.Fingerprint);
        Assert.Equal(0, api.Count("/v1/blocks/"));
    }

    [Fact]
    public async Task FingerprintChanged_FetchesBlocks()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "Alpha", "2026-09-19T12:00:00Z")))
            .OnGet("/v1/blocks/p1/children", Results(ParagraphBlock("b1", "new body")));

        var existing = new Dictionary<string, string>
        {
            ["notion://page/p1"] = "notion:2026-09-19T10:00:00Z"
        };

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), existing, CancellationToken.None);

        var doc = Assert.Single(result.Documents);
        Assert.Contains("new body", doc.TextContent);
        Assert.Equal("notion:2026-09-19T12:00:00Z", doc.Fingerprint);
    }

    [Fact]
    public async Task DatabaseRows_BecomeDocumentsWithProperties()
    {
        var row = """
            {"object":"page","id":"r1","last_edited_time":"2026-09-19T10:00:00Z",
             "properties":{"Name":{"type":"title","title":[{"plain_text":"Row One"}]},
                           "Status":{"type":"select","select":{"name":"Done"}}}}
            """;
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results("""{"object":"database","id":"db1","last_edited_time":"2026-09-19T10:00:00Z","title":[]}"""))
            .OnPost("/v1/databases/db1/query", Results(row))
            .OnGet("/v1/blocks/r1/children", Results(ParagraphBlock("b1", "row body")));

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), CancellationToken.None);

        var doc = Assert.Single(result.Documents);
        Assert.Equal("notion://page/r1", doc.UriReference);
        Assert.Equal("Row One", doc.Title);
        Assert.Contains("Name: Row One", doc.TextContent);
        Assert.Contains("Status: Done", doc.TextContent);
        Assert.Contains("row body", doc.TextContent);
    }

    [Fact]
    public async Task ItemFailure_BecomesWarning_SyncContinues()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "Broken"), Page("p2", "Fine")))
            .OnGet("/v1/blocks/p1/children", """{"object":"error","code":"restricted_resource"}""",
                HttpStatusCode.Forbidden)
            .OnGet("/v1/blocks/p2/children", Results(ParagraphBlock("b2", "fine body")));

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), CancellationToken.None);

        Assert.Single(result.Documents);
        Assert.Equal("notion://page/p2", result.Documents[0].UriReference);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("p1", warning);
    }

    [Fact]
    public async Task MaxPages_TruncatesWithWarning()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "A"), Page("p2", "B"), Page("p3", "C")))
            .OnGet("/v1/blocks/p1/children", Results(ParagraphBlock("b1", "a")));

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true, maxPages = 1 }), CancellationToken.None);

        Assert.Single(result.Documents);
        Assert.Contains(result.Warnings, w => w.Contains("maxPages") || w.Contains("truncated"));
    }

    [Fact]
    public async Task UnauthorizedProbe_ThrowsClearError()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"error","code":"unauthorized","message":"bad token"}""",
                HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut(api).FetchAsync(Source(new { hasKey = true }), CancellationToken.None));

        Assert.Contains("token", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, api.Count("/v1/search"));
    }

    [Fact]
    public async Task MissingSecret_ThrowsClearError()
    {
        var api = new FakeApi();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sut(api, token: null).FetchAsync(Source(new { hasKey = true }), CancellationToken.None));

        Assert.Contains("token", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task RootTraversal_PageAndChildDiscovered()
    {
        var childPageBlock = """
            {"id":"child-1","type":"child_page","has_children":true,"child_page":{"title":"Kid"}}
            """;
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnGet("/v1/pages/root-1", Page("root-1", "Root"))
            .OnGet("/v1/pages/child-1", Page("child-1", "Kid"))
            .OnGet("/v1/blocks/root-1/children", Results(childPageBlock, ParagraphBlock("b1", "root body")))
            .OnGet("/v1/blocks/child-1/children", Results(ParagraphBlock("b2", "kid body")));

        var source = Source(new { hasKey = true, rootPageIds = new[] { "root-1" } });
        var result = await Sut(api).FetchAsync(source, CancellationToken.None);

        Assert.Equal(2, result.Documents.Count);
        Assert.Equal("notion://page/root-1", result.Documents[0].UriReference);
        Assert.Equal("notion://page/child-1", result.Documents[1].UriReference);
        Assert.Contains("kid body", result.Documents[1].TextContent);
        Assert.Equal(0, api.Count("/v1/search"));
    }

    [Fact]
    public async Task RootDatabase_RowsDiscovered()
    {
        var row = """
            {"object":"page","id":"r1","last_edited_time":"2026-09-19T10:00:00Z",
             "properties":{"Name":{"type":"title","title":[{"plain_text":"R"}]}}}
            """;
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/databases/db-1/query", Results(row))
            .OnGet("/v1/blocks/r1/children", Results(ParagraphBlock("b1", "body")));

        var source = Source(new { hasKey = true, rootDatabaseIds = new[] { "db-1" } });
        var result = await Sut(api).FetchAsync(source, CancellationToken.None);

        var doc = Assert.Single(result.Documents);
        Assert.Equal("notion://page/r1", doc.UriReference);
        Assert.Equal(0, api.Count("/v1/search"));
    }

    [Fact]
    public async Task EmptyPage_StillIndexedByTitle()
    {
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "Empty One")))
            .OnGet("/v1/blocks/p1/children", Results());

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), CancellationToken.None);

        var doc = Assert.Single(result.Documents);
        Assert.Equal("Empty One", doc.Title);
    }

    [Fact]
    public async Task DeepNesting_RespectsMaxBlockDepth()
    {
        // Chain: page → toggle1 → toggle2 → ... each with has_children
        var api = new FakeApi()
            .OnGet("/v1/users/me", """{"object":"user"}""")
            .OnPost("/v1/search", Results(Page("p1", "Deep")));

        for (var i = 1; i <= 15; i++)
        {
            var toggle = $$$"""
                {"id":"t{{{i}}}","type":"toggle","has_children":true,
                 "toggle":{"rich_text":[{"plain_text":"lvl{{{i}}}"}]}}
                """;
            api.OnGet($"/v1/blocks/t{i}/children", Results(toggle.Replace("t" + i, "t" + (i + 1))));
        }
        api.OnGet("/v1/blocks/p1/children",
            Results("""{"id":"t1","type":"toggle","has_children":true,"toggle":{"rich_text":[{"plain_text":"lvl1"}]}}"""));

        var result = await Sut(api).FetchAsync(Source(new { hasKey = true }), CancellationToken.None);

        var doc = Assert.Single(result.Documents);
        Assert.Contains("lvl1", doc.TextContent);
        Assert.DoesNotContain("lvl15", doc.TextContent); // beyond maxBlockDepth=10
        Assert.Contains(result.Warnings, w => w.Contains("depth") || w.Contains("truncat") || w.Contains("limit"));
    }
}
