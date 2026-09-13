namespace KnowledgeHub.McpEngine.Activity;

/// <summary>Thread-safe ring-buffer implementation of <see cref="IMcpActivityFeed"/>.</summary>
public sealed class McpActivityFeed : IMcpActivityFeed
{
    private readonly object _gate = new();
    private readonly Queue<McpActivityEvent> _events;

    public McpActivityFeed(int capacity = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _events = new Queue<McpActivityEvent>(capacity);
    }

    public int Capacity { get; }

    public event Action<McpActivityEvent>? Published;

    public void Record(McpActivityEvent activityEvent)
    {
        lock (_gate)
        {
            while (_events.Count >= Capacity)
                _events.Dequeue();
            _events.Enqueue(activityEvent);
        }

        // Subscriber faults must never leak into the MCP pipeline.
        foreach (var handler in Published?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<McpActivityEvent>)handler)(activityEvent);
            }
            catch
            {
                // intentionally swallowed — observability must not break the server
            }
        }
    }

    public IReadOnlyList<McpActivityEvent> Snapshot()
    {
        lock (_gate)
            return _events.ToArray();
    }
}
