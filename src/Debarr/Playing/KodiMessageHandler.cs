using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;
using Wolverine.Attributes;

namespace Debarr.Playing;

/// <summary>Frames Kodi's JSON-RPC messages, which follow one another on the socket with no delimiter.</summary>
[WolverineIgnore]
public sealed class KodiMessageHandler : MessageHandlerBase
{
    private readonly Stream _stream;
    private readonly IAsyncEnumerator<JsonElement> _messages;
    private readonly ArrayBufferWriter<byte> _writeBuffer = new();

    public KodiMessageHandler(Stream stream)
        : base(new SystemTextJsonFormatter())
    {
        _stream = stream;
        _messages = JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, topLevelValues: true, cancellationToken: DisposalToken)
            .GetAsyncEnumerator(DisposalToken);
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    protected override async ValueTask<JsonRpcMessage?> ReadCoreAsync(CancellationToken cancellationToken)
    {
        while (await _messages.MoveNextAsync().AsTask().WaitAsync(cancellationToken))
        {
            var message = _messages.Current;
            if (message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // The formatter tells requests, results and notifications apart, and JsonRpc dispatches notifications to the local methods.
            return Formatter.Deserialize(new ReadOnlySequence<byte>(JsonMarshal.GetRawUtf8Value(message).ToArray()));
        }

        return null;
    }

    protected override ValueTask WriteCoreAsync(JsonRpcMessage content, CancellationToken cancellationToken)
    {
        _writeBuffer.ResetWrittenCount();
        Formatter.Serialize(_writeBuffer, content);

        return _stream.WriteAsync(_writeBuffer.WrittenMemory, cancellationToken);
    }

    protected override ValueTask FlushAsync(CancellationToken cancellationToken) => new(_stream.FlushAsync(cancellationToken));
}
