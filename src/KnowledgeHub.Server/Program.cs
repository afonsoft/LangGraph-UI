using KnowledgeHub.McpEngine;
using KnowledgeHub.Server;
using KnowledgeHub.Server.Api;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKnowledgeHubServer(builder.Configuration);
builder.Services.AddKnowledgeHubMcp(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHostedService<McpActivityBroadcastService>();

var app = builder.Build();

// RF-005: ensure the SQLite schema exists at startup and log the path.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
    db.Database.EnsureCreated();
    app.Logger.LogInformation("KnowledgeHub database ready at {Path}",
        app.Configuration.GetValue("Database:Path", "knowledgehub.db"));
}

// SPEC-05 RF-005: serve the hosted WASM client + deep-link fallback.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapSourcesApi();
app.MapSearchApi();
app.MapKnowledgeHubMcp();
app.MapHub<McpMonitorHub>("/hubs/mcp");
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
