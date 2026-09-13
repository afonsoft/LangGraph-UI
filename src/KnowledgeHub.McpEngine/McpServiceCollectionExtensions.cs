using KnowledgeHub.McpEngine.Activity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgeHub.McpEngine;

/// <summary>Composition root for the KnowledgeHub MCP server (SPEC-01).</summary>
public static class McpServiceCollectionExtensions
{
    public const int DefaultMaxConcurrentCallsPerSession = 8;

    /// <summary>
    /// Registers the official MCP server (dual transport: Streamable HTTP on /mcp,
    /// legacy SSE on /mcp/sse + /mcp/message) plus the activity feed used by the
    /// Monitor UI.
    /// </summary>
    public static IMcpServerBuilder AddKnowledgeHubMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IMcpActivityFeed>(_ => new McpActivityFeed(
            configuration.GetValue("Mcp:ActivityFeedCapacity", 500)));

        services.AddSingleton(_ => new SessionCallGate(
            configuration.GetValue("Mcp:MaxConcurrentCallsPerSession", DefaultMaxConcurrentCallsPerSession)));

        var builder = services
            .AddMcpServer()
            .WithHttpTransport(transport =>
            {
                // Stateful mode is required: legacy SSE never maps under Stateless=true,
                // and stateful sessions enable server→client notifications (SPEC-04).
                transport.Stateless = false;

                // Serves GET /mcp/sse + POST /mcp/message alongside Streamable HTTP on /mcp.
                // MCP9004: legacy SSE is obsolete upstream but required for Cursor/Claude
                // Desktop clients that only implement the HTTP+SSE transport.
#pragma warning disable MCP9004
                transport.EnableLegacySse = true;
#pragma warning restore MCP9004
            });

        services.AddOptions<McpServerOptions>()
            .Configure<IMcpActivityFeed, SessionCallGate>((options, feed, gate) =>
            {
                options.ServerInfo = new Implementation { Name = "knowledge-hub", Version = "0.1.0" };
                options.Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = true },
                    Resources = new ResourcesCapability { ListChanged = true }
                };

                options.Filters.Request.CallToolFilters.Add(McpActivityFilters.CreateToolCallFilter(feed, gate));
                options.Filters.Message.IncomingFilters.Add(McpActivityFilters.CreateRequestTelemetryFilter(feed));
            });

        return builder;
    }
}
