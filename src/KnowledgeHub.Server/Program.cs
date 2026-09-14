using KnowledgeHub.McpEngine;
using KnowledgeHub.Server;
using KnowledgeHub.Server.Api;
using KnowledgeHub.Server.Configuration;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Embeddings;
using KnowledgeHub.Server.Health;
using KnowledgeHub.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

// SPEC-20260914-config-validation: fail fast on invalid config before any work.
ConfigurationValidator.Validate(builder.Configuration);

// SPEC-20260914-graceful-shutdown: bounded drain window for in-flight requests.
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout =
    TimeSpan.FromSeconds(builder.Configuration.GetValue("Host:ShutdownTimeoutSeconds", 30)));

// SPEC-20260914-error-handling: RFC 7807 ProblemDetails for unhandled errors.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// SPEC-20260914-health-checks: /health/live + /health/ready.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<EmbeddingHealthCheck>("embeddings", tags: ["ready"])
    .AddCheck<IngestionHealthCheck>("ingestion", tags: ["ready"]);

builder.Services.AddKnowledgeHubServer(builder.Configuration);
builder.Services.AddKnowledgeHubMcp(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHostedService<McpActivityBroadcastService>();

var app = builder.Build();

// RF-005: apply pending migrations and log the path.
// SPEC-06 RF-002: default location is beside the executable; overridable via
// KnowledgeHub:DatabasePath / Database:Path. Clear error on read-only dirs.
// SPEC-20260914-efcore-migrations: Migrate() + baseline for EnsureCreated-era DBs.
DatabasePath.EnsureDirectory(app.Configuration);
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
    await DatabaseMigrator.MigrateAsync(db, app.Logger);
    app.Logger.LogInformation("KnowledgeHub database ready at {Path}",
        DatabasePath.Resolve(app.Configuration));

    // SPEC-20260914-embedding-dimension-guard: loud startup warning when the
    // persisted embeddings no longer match the configured provider.
    var embeddingProvider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
    try
    {
        await EmbeddingCompatibilityCheck.RunAsync(db, embeddingProvider, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Embedding compatibility check failed — continuing startup.");
    }
}

// SPEC-05 RF-005: serve the hosted WASM client + deep-link fallback.
// MapStaticAssets resolves the #[.{fingerprint}] tokens in index.html to the
// fingerprinted asset names (UseStaticFiles would serve the literal token).
app.UseExceptionHandler();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

app.MapStaticAssets();

app.MapSourcesApi();
app.MapSearchApi();
app.MapAskApi();
app.MapAgentApi();
app.MapToolsApi();
app.MapKnowledgeHubMcp();
app.MapHub<McpMonitorHub>("/hubs/mcp");
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
