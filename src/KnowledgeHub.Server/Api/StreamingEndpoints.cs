using System.Text.Json;
using KnowledgeHub.Server.Auth;
using KnowledgeHub.Server.Services;
using KnowledgeHub.Shared.Contracts;

namespace KnowledgeHub.Server.Api;

/// <summary>
/// SSE streaming endpoints (SPEC-20260914-streaming-answers RF-001):
/// POST /api/ask/stream and POST /api/agent/stream emit token/tool_start/
/// tool_end/awaiting_approval/done/error events; heartbeat every 15 s;
/// X-Accel-Buffering disabled for nginx; client disconnect cancels the work.
/// </summary>
public static class StreamingEndpoints
{
    public static void MapStreamingApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ask/stream", async (
            HttpContext http, AskRequest request,
            ISearchService search, IAnswerService answers) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { error = "question is required" });
                return;
            }
            var mode = SearchEndpoints.ParseMode(request.Mode);
            if (mode is null)
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { error = "mode must be hybrid | semantic | lexical" });
                return;
            }
            var generate = request.Generate ?? answers.IsConfigured;
            if (!generate || !answers.IsConfigured)
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { error = "chat provider not configured (Chat:Provider=none)" });
                return;
            }

            if (!Search.ResolvedSearchFilter.TryResolve(request.Filters, out var streamFilter, out var streamFilterError))
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { error = streamFilterError });
                return;
            }

            var ct = http.RequestAborted;
            var k = request.TopK is null or <= 0 ? SearchEndpoints.DefaultTopK : Math.Min(request.TopK.Value, SearchEndpoints.MaxTopK);
            var context = await search.SearchAsync(request.Question, k, request.SourceId, mode.Value, streamFilter, ct);
            await WriteSseAsync(http, answers.StreamAsync(request.Question, context, ct), ct);
        }).RequireAuthorization(AuthPolicies.Operational).RequireRateLimiting("llm");

        app.MapPost("/api/agent/stream", async (HttpContext http, AgentRequest request, IAgentService agent) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt) && request.Messages is not { Count: > 0 })
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { error = "prompt or messages[] is required" });
                return;
            }
            if (!agent.IsConfigured)
            {
                http.Response.StatusCode = 400;
                await http.Response.WriteAsJsonAsync(new { error = "agent requires a chat provider (Chat:Provider)" });
                return;
            }

            var ct = http.RequestAborted;
            await WriteSseAsync(http, agent.StreamAsync(request, ct), ct);
        }).RequireAuthorization(AuthPolicies.Operational).RequireRateLimiting("llm");
    }

    /// <summary>Writes the event stream; exceptions become a terminal "error" event.</summary>
    private static async Task WriteSseAsync(
        HttpContext http, IAsyncEnumerable<SseEvent> events, CancellationToken ct)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        await http.Response.StartAsync(ct);

        var seq = 0;
        var gate = new SemaphoreSlim(1, 1);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            try
            {
                while (await timer.WaitForNextTickAsync(stop.Token))
                {
                    await gate.WaitAsync(stop.Token);
                    try { await http.Response.WriteAsync(": keep-alive\n\n", stop.Token); await http.Response.Body.FlushAsync(stop.Token); }
                    finally { gate.Release(); }
                }
            }
            catch (OperationCanceledException) { }
        });

        try
        {
            await foreach (var e in events.WithCancellation(ct))
            {
                var payload = JsonSerializer.Serialize(new { seq = ++seq, data = e.Data }, JsonSerializerOptions.Web);
                await gate.WaitAsync(ct);
                try
                {
                    await http.Response.WriteAsync($"event: {e.Type}\ndata: {payload}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }
                finally
                {
                    gate.Release();
                }
            }
        }
        catch (OperationCanceledException) { /* client disconnected — stop quietly */ }
        catch (Exception ex)
        {
            var payload = JsonSerializer.Serialize(new { seq = ++seq, data = new { message = ex.Message } }, JsonSerializerOptions.Web);
            try { await http.Response.WriteAsync($"event: error\ndata: {payload}\n\n", CancellationToken.None); await http.Response.Body.FlushAsync(); }
            catch { /* connection already gone */ }
        }
        finally
        {
            await stop.CancelAsync();
            try { await heartbeat; } catch { }
        }
    }
}
