using System.Diagnostics;
using System.Text.Json;
using KnowledgeHub.McpEngine.Activity;
using KnowledgeHub.Server.Agent;
using KnowledgeHub.Server.Api;
using KnowledgeHub.Server.Chat;
using KnowledgeHub.Server.Data;
using KnowledgeHub.Server.Domain.Entities;
using KnowledgeHub.Server.Mcp;
using KnowledgeHub.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace KnowledgeHub.Server.Services;

/// <summary>
/// model→tools→model loop over the live tool catalog (SPEC-20260914-agent-chat-loop).
/// Mutating tools are hidden unless <c>allowWrite</c> is passed; gated tools suspend
/// the run into a pending <see cref="ToolApproval"/> resumable via
/// <see cref="ResumeAsync"/> (SPEC-20260914-hitl-tool-approval).
/// </summary>
public sealed class AgentService(
    IChatClient? chatClient,
    IServiceProvider services,
    IDynamicToolCatalog catalog,
    KnowledgeHubDbContext db,
    AgentOptions options,
    IMcpActivityFeed? feed,
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

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
        foreach (var m in request.Messages ?? [])
            messages.Add(new ChatMessage(
                m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User,
                m.Content));
        if (!string.IsNullOrWhiteSpace(request.Prompt))
            messages.Add(new ChatMessage(ChatRole.User, request.Prompt));
        if (messages.Count == 1)
            throw new ArgumentException("prompt or messages[] is required");

        var loop = await BuildLoopAsync(request, messages, cancellationToken);
        return await RunLoopAsync(client, loop, cancellationToken);
    }

    public async Task<AgentResponse> ResumeAsync(
        Guid approvalId, bool allowDenied = false, CancellationToken cancellationToken = default)
    {
        var client = chatClient
            ?? throw new ChatProviderException("agent_chat requires a chat provider (Chat:Provider)");

        var approval = await db.Approvals.FirstOrDefaultAsync(a => a.Id == approvalId, cancellationToken)
            ?? throw new KeyNotFoundException($"approval '{approvalId}' not found");
        if (approval.Status == "pending" && IsExpired(approval))
        {
            approval.Status = "expired";
            approval.ResolvedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        if (approval.Status is "pending" or "expired")
            throw new ConflictException($"approval '{approvalId}' is {approval.Status}");
        if (approval.Status == "denied" && !allowDenied)
            throw new ConflictException($"approval '{approvalId}' was denied");
        if (approval.ResumedAt is not null)
            throw new ConflictException($"approval '{approvalId}' already resumed");
        if (approval.StateJson is null)
            throw new ConflictException($"approval '{approvalId}' has no resumable state");

        approval.ResumedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var state = JsonSerializer.Deserialize<SuspendState>(approval.StateJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("corrupt approval state");

        var loop = await BuildLoopAsync(state.Request, RestoreMessages(state), cancellationToken);
        loop.Steps.AddRange(state.Steps);
        loop.Iterations = state.Iterations;
        loop.ToolCalls = state.ToolCalls;

        // Resolve the gated call: execute approved args, or inject the denial.
        var pending = state.PendingCall;
        var stepSw = Stopwatch.StartNew();
        object? result;
        var isError = false;
        if (approval.Status == "denied")
        {
            result = "ERROR: denied by user";
            isError = true;
        }
        else
        {
            var fn = loop.Functions.FirstOrDefault(f => f.Name == pending.Name);
            var args = approval.ApprovedArgsJson is { Length: > 0 } approved
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(approved)
                : pending.Args.Deserialize<Dictionary<string, object?>>();
            try
            {
                result = fn is null
                    ? $"ERROR: unknown tool '{pending.Name}'"
                    : await fn.InvokeAsync(new AIFunctionArguments(args), cancellationToken);
                isError = result?.ToString()?.StartsWith("ERROR:") == true;
            }
            catch (Exception ex)
            {
                isError = true;
                result = $"ERROR: {ex.Message}";
            }
        }

        loop.ToolCalls++;
        loop.Steps.Add(new AgentStep
        {
            Iteration = state.Iterations,
            Tool = pending.Name,
            ArgsSummary = Summarize(
                approval.ApprovedArgsJson is { Length: > 0 } approvedJson
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(approvedJson)
                    : pending.Args.Deserialize<Dictionary<string, object?>>()),
            IsError = isError,
            ElapsedMs = stepSw.Elapsed.TotalMilliseconds
        });
        loop.Messages.Add(new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(pending.CallId, result)]));

        return await RunLoopAsync(client, loop, cancellationToken);
    }

    private bool IsExpired(ToolApproval approval) =>
        approval.CreatedAt + TimeSpan.FromMinutes(options.ApprovalTimeoutMinutes) < DateTimeOffset.UtcNow;

    private bool RequiresApproval(CatalogTool tool) =>
        !tool.ReadOnly
        && (options.RequireApprovalFor.Contains("*")
            || options.RequireApprovalFor.Contains(tool.Name, StringComparer.OrdinalIgnoreCase));

    private async Task<LoopState> BuildLoopAsync(
        AgentRequest request, List<ChatMessage> messages, CancellationToken ct)
    {
        var maxIterations = request.MaxIterations is > 0
            ? Math.Min(request.MaxIterations.Value, options.MaxIterations)
            : options.MaxIterations;
        var allowlist = request.Tools is { Count: > 0 } t ? t.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

        var catalogTools = await catalog.GetToolsAsync(services, ct);
        var visible = catalogTools
            .Where(t => t.Name != "agent_chat") // no recursion
            .Where(t => allowlist is null || allowlist.Contains(t.Name))
            .Where(t => request.AllowWrite || t.ReadOnly)
            .ToList();

        return new LoopState
        {
            Messages = messages,
            Functions = visible
                .Select(t => (AIFunction)new CatalogToolAIFunction(t, services, options.MaxToolResultChars))
                .ToList(),
            ToolsByName = visible.ToDictionary(t => t.Name),
            MaxIterations = maxIterations,
            Request = request
        };
    }

    private async Task<AgentResponse> RunLoopAsync(
        IChatClient client, LoopState loop, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var answer = "";

        while (loop.Iterations < loop.MaxIterations && !loop.LimitReached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loop.Iterations++;

            var response = await client.GetResponseAsync(
                loop.Messages, new ChatOptions { Tools = [.. loop.Functions] }, cancellationToken);
            loop.Messages.AddRange(response.Messages);

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

                // HITL gate: mutating tool → suspend into a pending approval.
                if (loop.ToolsByName.TryGetValue(call.Name, out var gated) && RequiresApproval(gated))
                {
                    var approval = await SuspendAsync(loop, call, cancellationToken);
                    logger.LogInformation("agent_chat awaiting approval {ApprovalId} for {Tool}", approval.Id, call.Name);
                    return new AgentResponse
                    {
                        Answer = $"awaiting approval for tool '{call.Name}'",
                        Steps = loop.Steps,
                        ToolCalls = loop.Steps.Select(s => s.Tool).ToList(),
                        Iterations = loop.Iterations,
                        LatencyMs = sw.Elapsed.TotalMilliseconds,
                        LimitReached = false,
                        AwaitingApprovalId = approval.Id,
                        PendingTool = call.Name,
                        PendingArgsJson = approval.ArgumentsJson
                    };
                }

                loop.ToolCalls++;
                if (loop.ToolCalls > options.MaxToolCalls)
                {
                    loop.LimitReached = true;
                    break;
                }

                var fn = loop.Functions.FirstOrDefault(f => f.Name == call.Name);
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

                loop.Steps.Add(new AgentStep
                {
                    Iteration = loop.Iterations,
                    Tool = call.Name,
                    ArgsSummary = Summarize(call.Arguments),
                    IsError = isError,
                    ElapsedMs = stepSw.Elapsed.TotalMilliseconds
                });
                loop.Messages.Add(new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, result)]));
            }
        }

        if (loop.Iterations >= loop.MaxIterations)
            loop.LimitReached = true;
        if (loop.LimitReached)
            answer = string.IsNullOrEmpty(answer)
                ? "iteration limit reached"
                : answer + "\n\n(iteration limit reached)";

        logger.LogInformation(
            "agent_chat finished: {Iterations} iterations, {ToolCalls} tool calls, limit={LimitReached}",
            loop.Iterations, loop.ToolCalls, loop.LimitReached);

        return new AgentResponse
        {
            Answer = answer,
            Steps = loop.Steps,
            ToolCalls = loop.Steps.Select(s => s.Tool).ToList(),
            Iterations = loop.Iterations,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            LimitReached = loop.LimitReached
        };
    }

    /// <summary>Persists the suspended loop state + masked args; emits the feed event.</summary>
    private async Task<ToolApproval> SuspendAsync(
        LoopState loop, FunctionCallContent call, CancellationToken ct)
    {
        var argsElement = JsonSerializer.SerializeToElement(
            call.Arguments ?? new Dictionary<string, object?>(), JsonSerializerOptions.Web);
        var approval = new ToolApproval
        {
            ToolName = call.Name,
            ArgumentsJson = ApprovalService.MaskSensitive(argsElement).GetRawText(),
            RequestedBy = "agent",
            Status = "pending",
            StateJson = JsonSerializer.Serialize(new SuspendState(
                SnapshotMessages(loop.Messages),
                loop.Steps,
                loop.Iterations,
                loop.ToolCalls,
                loop.Request,
                new StoredCall(call.CallId, call.Name, argsElement)), JsonSerializerOptions.Web)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync(ct);

        feed?.Record(new McpActivityEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = McpActivityKind.ApprovalRequested,
            Transport = "agent",
            Method = "agent_chat",
            ToolName = call.Name
        });
        return approval;
    }

    // ---- suspended-state (de)serialization -----------------------------------

    private static List<StoredMessage> SnapshotMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(m => new StoredMessage(
            m.Role.Value,
            m.Text,
            m.Contents.OfType<FunctionCallContent>()
                .Select(c => new StoredCall(
                    c.CallId, c.Name,
                    JsonSerializer.SerializeToElement(c.Arguments, JsonSerializerOptions.Web)))
                .ToArray(),
            m.Contents.OfType<FunctionResultContent>()
                .Select(r => new StoredResult(r.CallId, r.Result?.ToString() ?? ""))
                .ToArray())).ToList();

    private static List<ChatMessage> RestoreMessages(SuspendState state) =>
        state.Messages.Select<StoredMessage, ChatMessage>(m =>
        {
            var contents = new List<AIContent>();
            if (!string.IsNullOrEmpty(m.Text))
                contents.Add(new TextContent(m.Text));
            contents.AddRange(m.Calls.Select(c => new FunctionCallContent(
                c.CallId, c.Name, c.Args.Deserialize<Dictionary<string, object?>>())));
            contents.AddRange(m.Results.Select(r => new FunctionResultContent(r.CallId, r.Result)));
            return new ChatMessage(
                m.Role == "assistant" ? ChatRole.Assistant
                    : m.Role == "system" ? ChatRole.System
                    : m.Role == "tool" ? ChatRole.Tool
                    : ChatRole.User,
                contents);
        }).ToList();

    private static AIFunctionArguments ToArguments(FunctionCallContent call) =>
        new(call.Arguments is null ? null : new Dictionary<string, object?>(call.Arguments));

    private static string Summarize(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
            return "{}";
        var json = JsonSerializer.Serialize(args, JsonSerializerOptions.Web);
        return json.Length <= 300 ? json : json[..300] + "…";
    }

    private sealed class LoopState
    {
        public required List<ChatMessage> Messages { get; init; }
        public required List<AIFunction> Functions { get; init; }
        public required Dictionary<string, CatalogTool> ToolsByName { get; init; }
        public required int MaxIterations { get; init; }
        public required AgentRequest Request { get; init; }
        public List<AgentStep> Steps { get; } = [];
        public int Iterations { get; set; }
        public int ToolCalls { get; set; }
        public bool LimitReached { get; set; }
    }

    private sealed record SuspendState(
        List<StoredMessage> Messages,
        List<AgentStep> Steps,
        int Iterations,
        int ToolCalls,
        AgentRequest Request,
        StoredCall PendingCall);

    private sealed record StoredMessage(
        string Role, string? Text, StoredCall[] Calls, StoredResult[] Results);

    private sealed record StoredCall(string CallId, string Name, JsonElement Args);

    private sealed record StoredResult(string CallId, string Result);
}
