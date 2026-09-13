using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace KnowledgeHub.Server.Mcp.Upstream;

/// <summary>
/// Lazy singleton <see cref="McpClient"/> to DeepWiki (SPEC-07 RF-001).
/// AutoDetect transport (Streamable HTTP, SSE fallback), Bearer auth when
/// configured, one reconnect attempt on transport/session failure.
/// </summary>
public sealed class DeepWikiUpstreamClient(
    IOptions<DeepWikiOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<DeepWikiUpstreamClient> logger) : IAsyncDisposable
{
    private readonly DeepWikiOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;

    /// <summary>Test seam — replaces transport construction.</summary>
    internal Func<HttpClientTransportOptions, IClientTransport>? TransportFactory { get; set; }

    /// <summary>Invoke an upstream tool; never throws — failures become IsError results.</summary>
    public async Task<CallToolResult> CallAsync(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken)
    {
        var dict = arguments?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        try
        {
            return await InvokeAsync(toolName, dict, cancellationToken);
        }
        catch (Exception first) when (first is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("DeepWiki call failed ({Message}); reconnecting once", first.Message);
            await ResetAsync();
            try
            {
                return await InvokeAsync(toolName, dict, cancellationToken);
            }
            catch (Exception second)
            {
                return ErrorResult($"deepwiki upstream error: {second.Message}");
            }
        }
    }

    private async Task<CallToolResult> InvokeAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        return await client.CallToolAsync(toolName, arguments, cancellationToken: timeout.Token);
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
                return _client;

            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(_options.Endpoint),
                TransportMode = HttpTransportMode.AutoDetect
            };
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                transportOptions.AdditionalHeaders ??= new Dictionary<string, string>();
                transportOptions.AdditionalHeaders["Authorization"] = $"Bearer {_options.ApiKey}";
            }

            var transport = TransportFactory?.Invoke(transportOptions)
                ?? new HttpClientTransport(transportOptions, loggerFactory);

            _client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "knowledge-hub-deepwiki-proxy", Version = "0.1.0" }
                },
                loggerFactory,
                cancellationToken);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static CallToolResult ErrorResult(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }]
    };

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _gate.Dispose();
    }
}
