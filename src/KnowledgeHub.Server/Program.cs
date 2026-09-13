using KnowledgeHub.McpEngine;
using KnowledgeHub.Server;
using KnowledgeHub.Server.Api;
using KnowledgeHub.Server.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKnowledgeHubServer(builder.Configuration);
builder.Services.AddKnowledgeHubMcp(builder.Configuration);

var app = builder.Build();

// RF-005: ensure the SQLite schema exists at startup and log the path.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeHubDbContext>();
    db.Database.EnsureCreated();
    app.Logger.LogInformation("KnowledgeHub database ready at {Path}",
        app.Configuration.GetValue("Database:Path", "knowledgehub.db"));
}

app.MapGet("/", () => "KnowledgeHub");
app.MapSourcesApi();
app.MapSearchApi();
app.MapKnowledgeHubMcp();

app.Run();

public partial class Program;
