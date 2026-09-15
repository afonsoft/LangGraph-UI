using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace KnowledgeHub.Tests.Integration;

// Covers SPEC-20260915-wasm-boot-proxy-fix RF-004/RF-006: extensionless
// /framework-assets/{fileName} mirror of wwwroot/_framework so proxies that
// block downloads by extension (.dat/.wasm/...) cannot break the WASM boot.
public class FrameworkAssetsTests
{
    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"kh-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = DbPath
                }));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            try { File.Delete(DbPath); } catch { /* best effort */ }
        }
    }

    private static IFileInfo PickAsset(IWebHostEnvironment env, string extension)
    {
        var file = env.WebRootFileProvider.GetDirectoryContents("_framework")
            .FirstOrDefault(f => !f.IsDirectory && f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(file); // test host must expose _framework static web assets
        return file;
    }

    private static bool HasSibling(IWebHostEnvironment env, string fileName, string suffix) =>
        env.WebRootFileProvider.GetFileInfo($"_framework/{fileName}{suffix}").Exists;

    [Fact]
    public async Task Get_KnownDatAsset_Anonymous_Returns200OctetStreamImmutableCache()
    {
        // Covers AC: remapped binary asset → 200 anonymously, octet-stream, immutable cache
        await using var factory = new Fixture();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var name = PickAsset(env, ".dat").Name;
        using var client = factory.CreateClient(); // no auth — must serve pre-login

        var response = await client.GetAsync($"/framework-assets/{name}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Get_KnownWasmAsset_Returns200()
    {
        // Covers AC: *.wasm binaries are also servable through the mirror route
        await using var factory = new Fixture();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var name = PickAsset(env, ".wasm").Name;
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/framework-assets/{name}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task Get_UnknownName_Returns404()
    {
        await using var factory = new Fixture();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/framework-assets/no-such-file.bin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("..foo")]
    [InlineData("..%2Fsecret")]
    [InlineData("a%2Fb")]
    [InlineData("..%5Cwin")]
    [InlineData("bad%20name")]
    public async Task Get_TraversalOrIllegalChars_Returns404(string badName)
    {
        // Covers AC: nothing outside _framework is ever served
        await using var factory = new Fixture();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/framework-assets/{badName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DotDot_NormalizedAway_NeverReturnsBinaryContent()
    {
        // "%2E%2E" is normalized to "/" before routing and lands on the SPA
        // fallback — acceptable (same exposure as MapFallbackToFile today),
        // but it must never surface _framework-external binary content.
        await using var factory = new Fixture();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/framework-assets/%2E%2E");

        Assert.NotEqual("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_AcceptEncoding_ServesCompressedSibling_WhenPresent()
    {
        // Covers RF-004 best-effort: publish emits .br/.gz next to every asset;
        // dev emits .gz — serve the negotiated variant when it exists.
        await using var factory = new Fixture();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var name = PickAsset(env, ".dat").Name;
        var hasBr = HasSibling(env, name, ".br");
        var hasGz = HasSibling(env, name, ".gz");
        Assert.True(hasBr || hasGz, "expected at least one compressed sibling for " + name);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/framework-assets/{name}");
        request.Headers.AcceptEncoding.ParseAdd("gzip, br");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encoding = response.Content.Headers.ContentEncoding.ToString();
        Assert.True(
            hasBr ? encoding == "br" : encoding == "gzip",
            $"expected Content-Encoding {(hasBr ? "br" : "gzip")}, got '{encoding}'");
    }
}
