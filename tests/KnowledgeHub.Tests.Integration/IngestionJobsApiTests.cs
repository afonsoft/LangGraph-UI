using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeHub.Shared.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KnowledgeHub.Tests.Integration;

// Covers SPEC-20260925-new-endpoints-integration-tests: ingestion job endpoints,
// reindex, cancel, and the async sync contract end-to-end over HTTP.
public class IngestionJobsApiTests : IClassFixture<IngestionJobsApiTests.Fixture>, IDisposable
{
    public sealed class Fixture : WebApplicationFactory<Program>
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"kh-jobs-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = DbPath
                }));
        }
    }

    private readonly HttpClient _client;
    private readonly string _dir;

    public IngestionJobsApiTests(Fixture factory)
    {
        _client = TestAuth.Login(factory);
        _dir = Path.Combine(Path.GetTempPath(), $"jobs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private async Task<KnowledgeSourceDto> SeedSource()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, $"doc-{Guid.NewGuid():N}.txt"), "job test content");
        var response = await _client.PostAsJsonAsync("/api/sources", new
        {
            name = $"jobs-{Guid.NewGuid():N}",
            type = "DocumentFile",
            configuration = new { path = _dir },
            isActive = true
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeSourceDto>())!;
    }

    private async Task<JsonElement> WaitTerminalJobAsync(Guid jobId)
    {
        // CI flake (PR #205): under parallel load a reindex exceeded the old
        // 30s budget — 120×500ms gives headroom without masking real hangs.
        for (var i = 0; i < 120; i++)
        {
            var job = await _client.GetFromJsonAsync<JsonElement>($"/api/ingestion/jobs/{jobId}");
            var status = job.GetProperty("status").GetString();
            if (status is "done" or "failed" or "cancelled")
                return job;
            await Task.Delay(500);
        }
        throw new TimeoutException($"job {jobId} did not reach terminal state");
    }

    [Fact]
    public async Task Sync_EnqueuesJob_AndJobReachesTerminal()
    {
        var source = await SeedSource();

        var enqueue = await _client.PostAsync($"/api/sources/{source.Id}/sync", null);
        Assert.Equal(HttpStatusCode.Accepted, enqueue.StatusCode);
        var body = await enqueue.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = body.GetProperty("jobId").GetGuid();
        Assert.False(jobId == Guid.Empty);
        Assert.Contains("existing", body.EnumerateObject().Select(p => p.Name));

        var job = await WaitTerminalJobAsync(jobId);
        Assert.Equal("done", job.GetProperty("status").GetString());
        Assert.True(job.GetProperty("docsProcessed").GetInt32() >= 1);
        Assert.True(job.GetProperty("chunksCreated").GetInt32() >= 1);
        Assert.Equal("sync", job.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Sync_WaitTrue_ReturnsSyncResult()
    {
        var source = await SeedSource();
        var response = await _client.PostAsync($"/api/sources/{source.Id}/sync?wait=true", null);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<SyncResultDto>())!;
        Assert.Equal("completed", result.Status);
        Assert.True(result.DocumentsProcessed >= 1);
    }

    [Fact]
    public async Task Jobs_List_FiltersBySource()
    {
        var a = await SeedSource();
        var b = await SeedSource();
        await _client.PostAsync($"/api/sources/{a.Id}/sync", null);
        await _client.PostAsync($"/api/sources/{b.Id}/sync", null);

        var jobs = await _client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/ingestion/jobs?sourceId={a.Id}&limit=10");
        Assert.NotEmpty(jobs!);
        Assert.All(jobs!, j => Assert.Equal(a.Id, j.GetProperty("sourceId").GetGuid()));
    }

    [Fact]
    public async Task Reindex_EnqueuesReindexJob()
    {
        var source = await SeedSource();
        await _client.PostAsync($"/api/sources/{source.Id}/sync?wait=true", null);

        var enqueue = await _client.PostAsync($"/api/sources/{source.Id}/reindex", null);
        Assert.Equal(HttpStatusCode.Accepted, enqueue.StatusCode);
        var jobId = (await enqueue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();

        var job = await WaitTerminalJobAsync(jobId);
        Assert.Equal("reindex", job.GetProperty("kind").GetString());
        Assert.Equal("done", job.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_NonExistent_Returns404()
    {
        var response = await _client.PostAsync($"/api/ingestion/jobs/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_TerminalJob_Returns409()
    {
        var source = await SeedSource();
        var enqueue = await _client.PostAsync($"/api/sources/{source.Id}/sync", null);
        var jobId = (await enqueue.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        await WaitTerminalJobAsync(jobId);

        var cancel = await _client.PostAsync($"/api/ingestion/jobs/{jobId}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
    }
}
