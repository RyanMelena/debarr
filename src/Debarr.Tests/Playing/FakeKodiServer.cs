using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Debarr.Tests.Playing;

/// <summary>
/// A loopback Kodi that serves one client at a time. It answers requests from literal JSON, records each request's parameters,
/// pushes raw notification bytes with no delimiter, and can drop the connection.
/// </summary>
public sealed class FakeKodiServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, string> _responses = new()
    {
        ["JSONRPC.Version"] = """{"version":{"major":13,"minor":5,"patch":0}}""",
        ["Application.GetProperties"] = """{"name":"Kodi","version":{"major":21,"minor":2}}""",
        ["Player.GetActivePlayers"] = "[]",
        ["JSONRPC.Ping"] = "\"pong\"",
    };
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _holds = new();
    private readonly ConcurrentQueue<(string Method, string Parameters)> _requests = new();
    private readonly Channel<bool> _connections = Channel.CreateUnbounded<bool>();
    private readonly Channel<bool> _disconnections = Channel.CreateUnbounded<bool>();
    private readonly CancellationTokenSource _stopped = new();
    private readonly Task _acceptTask;
    private int _connectionCount;
    private NetworkStream? _stream;

    public FakeKodiServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _acceptTask = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>How many clients have connected so far.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>The parameters of every request for the method, as JSON, in the order they arrived.</summary>
    public IReadOnlyList<string> RequestParameters(string method) =>
        [.. _requests.Where(request => request.Method == method).Select(request => request.Parameters)];

    /// <summary>The literal result to answer a request for the method with.</summary>
    public void Respond(string method, string resultJson) => _responses[method] = resultJson;

    /// <summary>Withholds every answer to the method until the returned source is completed.</summary>
    public TaskCompletionSource Hold(string method) =>
        _holds.GetOrAdd(method, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public Task WaitForConnectionAsync() =>
        _connections.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    public Task WaitForDisconnectionAsync() =>
        _disconnections.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>Writes the bytes as they are, so several messages can share one write with no separator.</summary>
    public Task SendRawAsync(string json) =>
        _stream is null ? Task.CompletedTask : _stream.WriteAsync(Encoding.UTF8.GetBytes(json), TestContext.Current.CancellationToken).AsTask();

    public void Drop() => _stream?.Close();

    public async ValueTask DisposeAsync()
    {
        await _stopped.CancelAsync();
        _stream?.Close();
        _listener.Stop();
        try
        {
            await _acceptTask;
        }
        catch (OperationCanceledException)
        {
        }

        _stopped.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopped.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopped.Token);
            }
            catch (Exception exception) when (_stopped.IsCancellationRequested || exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            using (client)
            {
                _stream = client.GetStream();
                Interlocked.Increment(ref _connectionCount);
                _connections.Writer.TryWrite(true);

                await ServeAsync(_stream, _stopped.Token);
                _stream = null;
                _disconnections.Writer.TryWrite(true);
            }
        }
    }

    private async Task ServeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, topLevelValues: true, cancellationToken: cancellationToken))
            {
                if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("id", out var id) || id.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                var method = message.TryGetProperty("method", out var methodElement) ? methodElement.GetString() ?? "" : "";
                _requests.Enqueue((method, message.TryGetProperty("params", out var parameters) ? parameters.GetRawText() : ""));

                if (_holds.TryGetValue(method, out var hold))
                {
                    await hold.Task.WaitAsync(cancellationToken);
                }

                var response = $$"""{"jsonrpc":"2.0","id":{{id.GetRawText()}},"result":{{_responses.GetValueOrDefault(method, "{}")}}}""";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
            }
        }
        catch (Exception)
        {
            // The client closed the socket, Drop closed it here, or the server stopped; each ends the session.
        }
    }
}
