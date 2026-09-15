using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// SPEC-20260915-wasm-boot-proxy-fix RF-004: extensionless mirror of
/// wwwroot/_framework so corporate proxies that block downloads by
/// extension (.dat/.wasm/...) cannot break the Blazor WASM boot.
/// Anonymous — same exposure as MapStaticAssets; strictly read-only,
/// single-segment names rooted under _framework.
/// </summary>
public static partial class FrameworkAssetsEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex ValidName();

    public static RouteGroupBuilder MapFrameworkAssetsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/framework-assets").AllowAnonymous();

        group.MapGet("/{fileName}", (string fileName, HttpContext http, IWebHostEnvironment env) =>
        {
            if (fileName.Contains("..", StringComparison.Ordinal) || !ValidName().IsMatch(fileName))
                return Results.NotFound();

            var root = env.WebRootFileProvider;
            var file = root.GetFileInfo($"_framework/{fileName}");
            if (!file.Exists || file.IsDirectory)
                return Results.NotFound();

            // Publish emits .br/.gz siblings for every asset; dev emits .gz.
            var compressed = TryCompressed(root, fileName, http.Request.Headers.AcceptEncoding.ToString(), out var encoding);

            http.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
            // Shared caches (corporate proxies) must key on Accept-Encoding.
            http.Response.Headers.Vary = "Accept-Encoding";
            if (compressed is not null)
            {
                http.Response.Headers.ContentEncoding = encoding;
                return Results.File(compressed.CreateReadStream(), "application/octet-stream");
            }
            return Results.File(file.CreateReadStream(), "application/octet-stream");
        });

        return group;
    }

    private static IFileInfo? TryCompressed(IFileProvider root, string fileName, string acceptEncoding, out string encoding)
    {
        foreach (var (suffix, enc) in new[] { (".br", "br"), (".gz", "gzip") })
        {
            if (!acceptEncoding.Contains(enc, StringComparison.OrdinalIgnoreCase))
                continue;
            var sibling = root.GetFileInfo($"_framework/{fileName}{suffix}");
            if (sibling.Exists && !sibling.IsDirectory)
            {
                encoding = enc;
                return sibling;
            }
        }
        encoding = "";
        return null;
    }
}
