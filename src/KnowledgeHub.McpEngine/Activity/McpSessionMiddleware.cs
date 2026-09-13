using System.Text;
using Microsoft.AspNetCore.Http;

namespace KnowledgeHub.McpEngine.Activity;

/// <summary>
/// Observes the MCP transport endpoints to record session lifecycle events
/// (SPEC-01 RF-003): session_opened/session_closed for legacy SSE connects
/// (/mcp/sse) and Streamable HTTP sessions (/mcp).
/// </summary>
public sealed class McpSessionMiddleware
{
    private const string SessionIdHeader = "Mcp-Session-Id";
    private readonly RequestDelegate _next;
    private readonly IMcpActivityFeed _feed;
    private readonly McpSessionRegistry _registry;

    public McpSessionMiddleware(RequestDelegate next, IMcpActivityFeed feed, McpSessionRegistry registry)
    {
        _next = next;
        _feed = feed;
        _registry = registry;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (HttpMethods.IsGet(context.Request.Method) &&
            path.Equals("/mcp/sse", StringComparison.OrdinalIgnoreCase))
        {
            await ObserveSseSessionAsync(context);
            return;
        }

        if (path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
            await ObserveStreamableHttpAsync(context);
        else
            await _next(context);
    }

    private async Task ObserveSseSessionAsync(HttpContext context)
    {
        string? sessionId = context.Request.Query["sessionId"];
        var opened = false;

        var originalBody = context.Response.Body;
        var sniff = new SessionIdSniffingStream(originalBody, id =>
        {
            sessionId = id;
            Record(McpActivityKind.SessionOpened, sessionId, "sse");
            opened = true;
        });

        context.Response.Body = sniff;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            if (!opened)
                Record(McpActivityKind.SessionOpened, sessionId, "sse");
            Record(McpActivityKind.SessionClosed, sessionId, "sse");
            _registry.Unregister(sessionId);
        }
    }

    private async Task ObserveStreamableHttpAsync(HttpContext context)
    {
        var requestSessionId = context.Request.Headers[SessionIdHeader].ToString();
        await _next(context);

        var responseSessionId = context.Response.Headers[SessionIdHeader].ToString();
        var sessionId = string.IsNullOrEmpty(requestSessionId) ? responseSessionId : requestSessionId;

        // initialize issues a new Mcp-Session-Id; DELETE terminates it.
        if (HttpMethods.IsPost(context.Request.Method) && string.IsNullOrEmpty(requestSessionId) && !string.IsNullOrEmpty(responseSessionId))
            Record(McpActivityKind.SessionOpened, responseSessionId, "streamable-http");
        else if (HttpMethods.IsDelete(context.Request.Method) && !string.IsNullOrEmpty(sessionId))
        {
            Record(McpActivityKind.SessionClosed, sessionId, "streamable-http");
            _registry.Unregister(sessionId);
        }
    }

    private void Record(McpActivityKind kind, string? sessionId, string transport) =>
        _feed.Record(new McpActivityEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            SessionId = sessionId,
            Transport = transport
        });

    /// <summary>
    /// Response stream wrapper that scans the first bytes of the SSE stream for the
    /// <c>event: endpoint</c> frame carrying <c>sessionId=</c>, so session events
    /// carry the real SDK-generated session id.
    /// </summary>
    private sealed class SessionIdSniffingStream : Stream
    {
        private const int SniffWindow = 4096;
        private readonly Stream _inner;
        private readonly Action<string> _onSessionId;
        private readonly byte[] _buffer = new byte[SniffWindow];
        private int _buffered;
        private bool _found;

        public SessionIdSniffingStream(Stream inner, Action<string> onSessionId)
        {
            _inner = inner;
            _onSessionId = onSessionId;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            Sniff(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            Sniff(buffer.Span);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            Sniff(buffer, offset, count);
        }

        private void Sniff(byte[] buffer, int offset, int count) => Sniff(buffer.AsSpan(offset, count));

        private void Sniff(ReadOnlySpan<byte> data)
        {
            if (_found || _buffered >= SniffWindow)
                return;

            var copy = Math.Min(data.Length, SniffWindow - _buffered);
            data[..copy].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += copy;

            var text = Encoding.UTF8.GetString(_buffer, 0, _buffered);
            const string marker = "sessionId=";
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return;

            var start = idx + marker.Length;
            var end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '-' or '_' or '='))
                end++;

            if (end > start)
            {
                _found = true;
                _onSessionId(text[start..end]);
            }
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
