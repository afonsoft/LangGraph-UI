using System.Diagnostics;
using System.Text.Json;
using KnowledgeHub.Server.Agent;
using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Shared.Contracts;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// model→tools→model loop over the live tool catalog (SPEC-20260914-agent-chat-loop).
/// Mutating tools are hidden unless <c>allowWrite</c> is passed; each internal call
/// is recorded on the MCP activity feed and summarized in <c>steps[]</c>.
/// </summary>
public sealed class AgentService(
    IChatClient? chatClient,
    IServiceProvider services,
    IDynamicToolCatalog catalog,
    AgentOptions options,
    ILogger<AgentService> logger) : IAgentService
{
    private const string SystemPrompt =
        "You are the KnowledgeHub agent. Use the available tools to research the " +
        "knowledge base, then answer concisely and cite source/uri of what you used.";

    public bool IsConfigured => chatClient is not null;

    public async Task<AgentResponse> RunAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        var client = chatClient
            ?? throw new ChatProviderException("agent_chat requires a chat provider (Chat:Provider)");

        var sw = Stopwatch.StartNew();
        var maxIterations = request.MaxIterations is > 0
            ? Math.Min(request.MaxIterations.Value, options.MaxIterations)
            : options.MaxIterations;
        var allowlist = request.Tools is { Count: > 0 } t ? t.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

        var catalogTools = await catalog.GetToolsAsync(services, cancellationToken);
        var functions = catalogTools
            .Where(t => t.Name != "agent_chat") // no recursion
            .Where(t => allowlist is null || allowlist.Contains(t.Name))
            .Where(t => request.AllowWrite || t.ReadOnly)
            .Select(t => new CatalogToolAIFunction(t, services, options.MaxToolResultChars))
            .ToList<AIFunction>();

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
        foreach (var m in request.Messages ?? [])
            messages.Add(new ChatMessage(
                m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User,
                m.Content));
        if (!string.IsNullOrWhiteSpace(request.Prompt))
            messages.Add(new ChatMessage(ChatRole.User, request.Prompt));
        if (messages.Count == 1)
            throw new ArgumentException("prompt or messages[] is required");
        var chatOptions = new ChatOptions { Tools = [.. functions] };

        var steps = new List<AgentStep>();
        var iterations = 0;
        var toolCalls = 0;
        var limitReached = false;
        var answer = "";

        while (iterations < maxIterations && !limitReached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations++;

            var response = await client.GetResponseAsync(messages, chatOptions, cancellationToken);
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (calls.Count == 0)
            {
                answer = response.Text?.Trim() ?? "";
                break;
            }

            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                toolCalls++;
                if (toolCalls > options.MaxToolCalls)
                {
                    limitReached = true;
                    break;
                }

                var fn = functions.FirstOrDefault(f => f.Name == call.Name);
                var stepSw = Stopwatch.StartNew();
                object? result;
                var isError = false;
                try
                {
                    result = fn is null
                        ? $"ERROR: unknown tool '{call.Name}'"
                        : await fn.InvokeAsync(ToArguments(call), cancellationToken);
                    isError = result?.ToString()?.StartsWith("ERROR:") == true;
                }
                catch (Exception ex)
                {
                    isError = true;
                    result = $"ERROR: {ex.Message}";
                }

                steps.Add(new AgentStep
                {
                    Iteration = iterations,
                    Tool = call.Name,
                    ArgsSummary = Summarize(call.Arguments),
                    IsError = isError,
                    ElapsedMs = stepSw.Elapsed.TotalMilliseconds
                });
                messages.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, result)]));
            }
        }

        if (iterations >= maxIterations || limitReached)
            limitReached = true;
        if (limitReached)
            answer = string.IsNullOrEmpty(answer)
                ? "iteration limit reached"
                : answer + "\n\n(iteration limit reached)";

        logger.LogInformation(
            "agent_chat finished: {Iterations} iterations, {ToolCalls} tool calls, limit={LimitReached}",
            iterations, toolCalls, limitReached);

        return new AgentResponse
        {
            Answer = answer,
            Steps = steps,
            ToolCalls = steps.Select(s => s.Tool).ToList(),
            Iterations = iterations,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            LimitReached = limitReached
        };
    }

    private static AIFunctionArguments ToArguments(FunctionCallContent call) =>
        new(call.Arguments is null ? null : new Dictionary<string, object?>(call.Arguments));

    private static string Summarize(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
            return "{}";
        var json = JsonSerializer.Serialize(args, JsonSerializerOptions.Web);
        return json.Length <= 300 ? json : json[..300] + "…";
    }
}
