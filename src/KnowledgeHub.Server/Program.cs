using KnowledgeHub.McpEngine;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKnowledgeHubMcp(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "KnowledgeHub");
app.MapKnowledgeHubMcp();

app.Run();

public partial class Program;
