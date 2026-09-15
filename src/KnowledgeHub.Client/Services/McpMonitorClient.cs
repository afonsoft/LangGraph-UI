using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace KnowledgeHub.Client.Services;

/// <summary>Hub event DTOs mirrored from SPEC-05 §5.</summary>
public sealed record SessionOpenedEvent(string SessionId, DateTimeOffset ConnectedAt);
public sealed record SessionClosedEvent(string SessionId);
public sealed record ActivityEvent(
    DateTimeOffset Timestamp, string? SessionId, string? Method,
    string? Detail, double? DurationMs, bool? Succeeded);

/// <summary>
/// Wraps the SignalR connection to /hubs/mcp (SPEC-05 RF-003).
/// Auto-reconnect with backoff; caller registers handlers before StartAsync.
/// SPEC-20260914-signalr-hub-resilience: WS|LongPolling only (SSE is never
/// supported in WASM), OperationCanceledException on StartAsync is benign
/// (dispose/navigation mid-connect), LastError surfaces the failure detail.
/// </summary>
public sealed class McpMonitorClient(NavigationManager nav) : IAsyncDisposable
{
    private readonly HubConnection _connection = new HubConnectionBuilder()
        .WithUrl(nav.ToAbsoluteUri("/hubs/mcp"),
            HttpTransportType.WebSockets | HttpTransportType.LongPolling)
        .WithAutomaticReconnect()
        .Build();

    // Registered once — a post-failure "Reconectar" calls StartAsync again and
    // re-subscribing On(...) would duplicate every event invocation.
    private bool _handlersRegistered;

    public HubConnectionState State => _connection.State;

    /// <summary>Last connect failure (per-transport aggregate); null after a successful connect.</summary>
    public string? LastError { get; private set; }

    public event Action? StateChanged;
    public event Action<SessionOpenedEvent>? SessionOpened;
    public event Action<SessionClosedEvent>? SessionClosed;
    public event Action<ActivityEvent>? Activity;
    public event Action<IReadOnlyList<object>>? Snapshot;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connection.State != HubConnectionState.Disconnected)
            return;
        if (!_handlersRegistered)
        {
            _connection.On<SessionOpenedEvent>("SessionOpened", e => SessionOpened?.Invoke(e));
            _connection.On<SessionClosedEvent>("SessionClosed", e => SessionClosed?.Invoke(e));
            _connection.On<ActivityEvent>("Activity", e => Activity?.Invoke(e));
            _connection.On<IReadOnlyList<object>>("Snapshot", s => Snapshot?.Invoke(s));
            _connection.Reconnecting += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
            _connection.Reconnected += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
            _connection.Closed += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
            _handlersRegistered = true;
        }

        try
        {
            await _connection.StartAsync(ct);
            LastError = null;
        }
        catch (OperationCanceledException)
        {
            // RF-001: dispose or navigation mid-connect is benign.
            return;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
