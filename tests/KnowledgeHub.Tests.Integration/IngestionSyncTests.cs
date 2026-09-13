using System.Net;
using System.Net.Http.Json;
using KnowledgeHub.Shared.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace KnowledgeHub.Tests.Integration;

// Covers SPEC-03 ACs: vault sync indexes docs/chunks, hash short-circuit, deletion propagates.
public class IngestionSyncTests : IClassFixture<IngestionSyncTests.Fixture>, IDisposable
{
    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"kh-ing-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = DbPath,
                    ["Ingestion:MaxTokens"] = "500",
                    ["Ingestion:OverlapTokens"] = "50"
                }));
        }
    }

    private readonly HttpClient _client;
    private readonly string _vault;

    public IngestionSyncTests(Fixture factory)
    {
        _client = factory.CreateClient();
        _vault = Path.Combine(Path.GetTempPath(), $"vault-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vault);
    }

    public void Dispose()
    {
        try { Directory.Delete(_vault, recursive: true); } catch { }
    }

    private async Task<KnowledgeSourceDto> CreateVaultSource()
    {
        var response = await _client.PostAsJsonAsync("/api/sources", new
        {
            name = $"vault-{Guid.NewGuid():N}",
            type = "ObsidianVault",
            configuration = new { path = _vault },
            isActive = true
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeSourceDto>())!;
    }

    [Fact]
    public async Task Sync_IndexesMarkdownFiles_WithChunks()
    {
        await File.WriteAllTextAsync(Path.Combine(_vault, "a.md"), "# Nota A\n\nconteúdo sobre arquitetura limpa");
        await File.WriteAllTextAsync(Path.Combine(_vault, "b.md"), "---\ntags: [x]\n---\n# Nota B\n\noutro conteúdo");
        await File.WriteAllTextAsync(Path.Combine(_vault, "c.md"), "sem header");

        var source = await CreateVaultSource();
        var response = await _client.PostAsync($"/api/sources/{source.Id}/sync", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SyncResultDto>();
        Assert.Equal("completed", result!.Status);
        Assert.Equal(3, result.DocumentsProcessed);
        Assert.True(result.ChunksCreated >= 3);

        var docs = await _client.GetFromJsonAsync<List<KnowledgeDocumentDto>>($"/api/sources/{source.Id}/documents");
        Assert.Equal(3, docs!.Count);
    }

    [Fact]
    public async Task Sync_UnchangedFile_IsSkipped_AndDeletedFile_IsRemoved()
    {
        var file = Path.Combine(_vault, "keep.md");
        await File.WriteAllTextAsync(file, "# Keep\n\nestável");
        var source = await CreateVaultSource();

        await _client.PostAsync($"/api/sources/{source.Id}/sync", null);
        var second = await (await _client.PostAsync($"/api/sources/{source.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResultDto>();
        Assert.Equal(1, second!.DocumentsSkipped);
        Assert.Equal(0, second.DocumentsProcessed);

        File.Delete(file);
        var third = await (await _client.PostAsync($"/api/sources/{source.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResultDto>();
        Assert.Equal(1, third!.DocumentsRemoved);

        var docs = await _client.GetFromJsonAsync<List<KnowledgeDocumentDto>>($"/api/sources/{source.Id}/documents");
        Assert.Empty(docs!);
    }

    [Fact]
    public async Task Sync_ObsidianDirAndHiddenDirs_AreExcluded()
    {
        Directory.CreateDirectory(Path.Combine(_vault, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_vault, ".hidden"));
        await File.WriteAllTextAsync(Path.Combine(_vault, ".obsidian", "config.md"), "secret");
        await File.WriteAllTextAsync(Path.Combine(_vault, ".hidden", "x.md"), "hidden");
        await File.WriteAllTextAsync(Path.Combine(_vault, "visible.md"), "# V\n\nok");

        var source = await CreateVaultSource();
        var result = await (await _client.PostAsync($"/api/sources/{source.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResultDto>();

        Assert.Equal(1, result!.DocumentsProcessed);
    }

    [Fact]
    public async Task Sync_MissingPath_ReturnsFailed()
    {
        var response = await _client.PostAsJsonAsync("/api/sources", new
        {
            name = $"gone-{Guid.NewGuid():N}",
            type = "ObsidianVault",
            configuration = new { path = "/nonexistent/definitely" }
        });
        var source = (await response.Content.ReadFromJsonAsync<KnowledgeSourceDto>())!;

        var result = await (await _client.PostAsync($"/api/sources/{source.Id}/sync", null))
            .Content.ReadFromJsonAsync<SyncResultDto>();
        Assert.Equal("failed", result!.Status);
    }

    [Fact]
    public async Task Search_ReturnsRankedResults_AfterSync()
    {
        await File.WriteAllTextAsync(Path.Combine(_vault, "vectors.md"),
            "# Busca Vetorial\n\nembeddings vetores similaridade cosseno busca semântica");
        await File.WriteAllTextAsync(Path.Combine(_vault, "cake.md"),
            "# Bolo\n\nreceita de bolo de chocolate com farinha");

        var source = await CreateVaultSource();
        await _client.PostAsync($"/api/sources/{source.Id}/sync", null);

        var search = await _client.GetFromJsonAsync<SearchResponse>("/api/search?query=embeddings vetores&topK=5");
        Assert.NotEmpty(search!.Results);
        Assert.Equal("Busca Vetorial", search.Results[0].DocumentTitle);
        Assert.True(search.Results[0].Score >= search.Results[^1].Score);
    }
}
