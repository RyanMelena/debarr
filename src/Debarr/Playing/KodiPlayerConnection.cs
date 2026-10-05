using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Channels;
using Debarr.Detecting;
using Debarr.Extensions;
using StreamJsonRpc;

namespace Debarr.Playing;

/// <summary>
/// A persistent JSON-RPC connection to Kodi, whose requests and notifications share one socket.
/// Each Player.OnPlay for a video becomes a playback started event once Player.GetItem has named the file.
/// Each subscription to <see cref="Events"/> opens its own connection.
/// </summary>
public sealed partial class KodiPlayerConnection
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    private static readonly string[] ItemProperties = ["file", "title", "showtitle", "year", "season", "episode", "streamdetails"];

    private readonly Player _player;

    private readonly KodiEndpoint _endpoint;

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<KodiPlayerConnection> _logger;

    public KodiPlayerConnection(Player player, KodiEndpoint endpoint, TimeProvider timeProvider, ILogger<KodiPlayerConnection> logger)
    {
        _player = player;
        _endpoint = endpoint;
        _timeProvider = timeProvider;
        _logger = logger;
        Events = Observable.Create<PlayerConnectionEvent>(RunAsync);
    }

    public IObservable<PlayerConnectionEvent> Events { get; }

    private TimeSpan RequestTimeout => TimeSpan.FromSeconds(_endpoint.RequestTimeoutSeconds);

    /// <summary>Connects and serves sessions one after another, with backoff between them, until the subscription is disposed.</summary>
    private async Task RunAsync(IObserver<PlayerConnectionEvent> observer, CancellationToken cancellationToken)
    {
        var backoff = InitialBackoff;

        // The state since the connection was last lost; null while connected.
        PlayerConnectionState.Disconnected? disconnected = null;

        observer.OnNext(CreateStateChangedEvent(new PlayerConnectionState.Connecting(_timeProvider.GetUtcNow())));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ServeAsync(
                    observer,
                    () =>
                    {
                        backoff = InitialBackoff;
                        disconnected = null;
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                var now = _timeProvider.GetUtcNow();
                var firstFailure = disconnected is null;
                disconnected = new PlayerConnectionState.Disconnected(exception.Message, disconnected?.Since ?? now, now + backoff);
                observer.OnNext(CreateStateChangedEvent(disconnected));

                // The first failure warns; each retry that fails after it logs at Debug, so a player that stays off writes one warning.
                LogDisconnected(firstFailure ? LogLevel.Warning : LogLevel.Debug, _player.Name, exception.Message, backoff.ToDisplayText());

                try
                {
                    await Task.Delay(backoff, _timeProvider, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, backoff.Ticks * 2));
                observer.OnNext(CreateStateChangedEvent(disconnected with { RetryAt = null }));
            }
        }
    }

    /// <summary>Runs one session, from connecting to the socket until the socket or a ping fails.</summary>
    private async Task ServeAsync(IObserver<PlayerConnectionEvent> observer, Action onConnected, CancellationToken cancellationToken)
    {
        using var client = await ConnectAsync(cancellationToken);

        // JsonRpc can call the notification method on several threads at once, so the reports queue here and a single loop reads them.
        var onPlays = Channel.CreateUnbounded<(int? PlayerId, DateTimeOffset OccurredAt)>(new UnboundedChannelOptions { SingleReader = true });
        // Kodi's JSON-RPC API has no $/cancelRequest method, so a cancelled request sends Kodi nothing.
        using var rpc = new JsonRpc(new KodiMessageHandler(client.GetStream())) { CancellationStrategy = null };
        rpc.AddLocalRpcMethod("Player.OnPlay", new Action<JsonElement, string?>((data, sender) =>
        {
            if (data.GetPropertyOrDefault("item")?.GetNonEmptyStringOrDefault("type") is "song" or "picture")
            {
                return;
            }

            // Kodi sends playerid -1 while the player is still starting.
            var playerId = data.GetPropertyOrDefault("player")?.GetInt32OrDefault("playerid");
            onPlays.Writer.TryWrite((playerId >= 0 ? playerId : null, _timeProvider.GetUtcNow()));
        }));
        rpc.StartListening();

        var version = await HandshakeAsync(rpc, cancellationToken);
        observer.OnNext(CreateStateChangedEvent(new PlayerConnectionState.Connected(version, _timeProvider.GetUtcNow())));
        LogConnected(_player.Name, _endpoint.Host, _endpoint.Port, version);
        onConnected();

        using var sessionEnd = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var forwarding = ForwardPlaybackStartedAsync(rpc, onPlays.Reader, observer, sessionEnd.Token);
        var pinging = PingAsync(rpc, sessionEnd.Token);
        var ended = await Task.WhenAny(rpc.Completion, forwarding, pinging);

        await sessionEnd.CancelAsync();
        try
        {
            await Task.WhenAll(forwarding, pinging);
        }
        catch (OperationCanceledException) when (sessionEnd.IsCancellationRequested)
        {
        }

        // Forwarding has ended, so the caller's next state event never overlaps a playback started event.
        // Rethrows the failure that ended the session. A clean close from Kodi throws below.
        await ended;
        cancellationToken.ThrowIfCancellationRequested();
        throw new KodiRequestException("The connection to Kodi ended.");
    }

    private async Task ForwardPlaybackStartedAsync(
        JsonRpc rpc,
        ChannelReader<(int? PlayerId, DateTimeOffset OccurredAt)> onPlays,
        IObserver<PlayerConnectionEvent> observer,
        CancellationToken cancellationToken)
    {
        await foreach (var (playerId, occurredAt) in onPlays.ReadAllAsync(cancellationToken))
        {
            try
            {
                if (await ReadPlaybackStartedAsync(rpc, playerId, occurredAt, cancellationToken) is { } playbackStarted)
                {
                    observer.OnNext(playbackStarted);
                }
            }
            catch (KodiRequestException exception)
            {
                LogReadPlayingFailed(_player.Name, exception.Message);
            }
        }
    }

    /// <summary>The playback started event for the item the player plays; null when no video player is active or the item has no file.</summary>
    private async Task<PlaybackStartedEvent?> ReadPlaybackStartedAsync(JsonRpc rpc, int? playerId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if ((playerId ?? await FindVideoPlayerIdAsync(rpc, cancellationToken)) is not { } videoPlayerId)
        {
            LogNoActiveVideoPlayer(_player.Name);
            return null;
        }

        var result = await RequestAsync(
            rpc,
            "Player.GetItem",
            new Dictionary<string, object?> { ["playerid"] = videoPlayerId, ["properties"] = ItemProperties },
            cancellationToken);

        if (result.GetPropertyOrDefault("item") is not { } item || item.GetNonEmptyStringOrDefault("file") is not { } file)
        {
            LogItemWithNoFile(_player.Name);
            return null;
        }

        return new PlaybackStartedEvent(_player.Id, _player.Name, _endpoint.ToPlayerPath(file), GetAspectRatio(item), GetTitle(item), occurredAt);
    }

    /// <summary>The active video player's id, or null when none is active.</summary>
    private async Task<int?> FindVideoPlayerIdAsync(JsonRpc rpc, CancellationToken cancellationToken)
    {
        var players = await RequestAsync(rpc, "Player.GetActivePlayers", null, cancellationToken);

        return players.ValueKind == JsonValueKind.Array
            ? players.EnumerateArray().FirstOrDefault(player => player.GetNonEmptyStringOrDefault("type") == "video").GetInt32OrDefault("playerid")
            : null;
    }

    private async Task PingAsync(JsonRpc rpc, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(_endpoint.PingIntervalSeconds), _timeProvider, cancellationToken);
            await RequestAsync(rpc, "JSONRPC.Ping", null, cancellationToken);
        }
    }

    private async Task<TcpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(RequestTimeout);
            await client.ConnectAsync(_endpoint.Host, _endpoint.Port, connectTimeout.Token);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            return client;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new KodiRequestException($"Connecting to {_endpoint.Host}:{_endpoint.Port} timed out.");
        }
        catch (SocketException exception)
        {
            client.Dispose();
            throw new KodiRequestException($"Connecting to {_endpoint.Host}:{_endpoint.Port} failed: {exception.Message}", exception);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>The player's name and version, such as "Kodi 21.2 (JSON-RPC 13.5.0)".</summary>
    private async Task<string> HandshakeAsync(JsonRpc rpc, CancellationToken cancellationToken)
    {
        var jsonRpcVersion = await RequestAsync(rpc, "JSONRPC.Version", null, cancellationToken);
        var properties = await RequestAsync(
            rpc,
            "Application.GetProperties",
            new Dictionary<string, object?> { ["properties"] = new[] { "version", "name" } },
            cancellationToken);

        var name = properties.GetNonEmptyStringOrDefault("name") ?? "Kodi";
        var applicationVersion = properties.GetPropertyOrDefault("version") is { } application
            ? $"{application.GetInt32OrDefault("major") ?? 0}.{application.GetInt32OrDefault("minor") ?? 0}"
            : "";
        var protocolVersion = jsonRpcVersion.GetPropertyOrDefault("version") is { } protocol
            ? $"{protocol.GetInt32OrDefault("major") ?? 0}.{protocol.GetInt32OrDefault("minor") ?? 0}.{protocol.GetInt32OrDefault("patch") ?? 0}"
            : "";

        return $"{name} {applicationVersion} (JSON-RPC {protocolVersion})".Replace("  ", " ");
    }

    private async Task<JsonElement> RequestAsync(JsonRpc rpc, string method, object? parameters, CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(RequestTimeout);
        try
        {
            // A sent request ignores its token, so the wait for the answer carries the timeout and the cancellation.
            return await rpc.InvokeWithParameterObjectAsync<JsonElement>(method, parameters, requestTimeout.Token).WaitAsync(requestTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new KodiRequestException($"{method} timed out after {RequestTimeout.TotalSeconds:0} s.");
        }
        catch (Exception exception) when (exception is RemoteInvocationException or RemoteMethodNotFoundException)
        {
            throw new KodiRequestException($"{method} failed: {exception.Message}", exception);
        }
        catch (ConnectionLostException exception)
        {
            throw new KodiRequestException($"{method} failed: the connection was lost.", exception);
        }
    }

    private PlayerConnectionStateChangedEvent CreateStateChangedEvent(PlayerConnectionState state) => new(_player.Id, _player.Name, state);

    /// <summary>A movie as "title (year)", an episode as "showtitle S01E02 title", and anything else by its title or label.</summary>
    private static string? GetTitle(JsonElement item)
    {
        var title = item.GetNonEmptyStringOrDefault("title");
        return item.GetNonEmptyStringOrDefault("type") switch
        {
            "movie" when title is not null && item.GetInt32OrDefault("year") is int year and > 0 => $"{title} ({year})",
            "episode" when title is not null
                && item.GetNonEmptyStringOrDefault("showtitle") is { } showTitle
                && item.GetInt32OrDefault("season") is int season and >= 0
                && item.GetInt32OrDefault("episode") is int episode and >= 0
                => $"{showTitle} S{season:00}E{episode:00} {title}",
            _ => title ?? item.GetNonEmptyStringOrDefault("label"),
        };
    }

    /// <summary>The first video stream's aspect ratio, or null when Kodi reports none.</summary>
    private static AspectRatio? GetAspectRatio(JsonElement item) =>
        item.GetPropertyOrDefault("streamdetails")?.GetPropertyOrDefault("video") is { ValueKind: JsonValueKind.Array } video
        && video.GetArrayLength() > 0
        && video[0].GetPropertyOrDefault("aspect") is { ValueKind: JsonValueKind.Number } aspectRatioElement
        && aspectRatioElement.TryGetDouble(out var aspectRatio)
        && AspectRatio.IsValid(aspectRatio)
            ? new AspectRatio(aspectRatio)
            : null;

    /// <summary>A Kodi connection or request that failed, described for the operator.</summary>
    private sealed class KodiRequestException(string message, Exception? innerException = null) : Exception(message, innerException);

    [LoggerMessage("{Player} disconnected. {Error} Retrying in {Delay}.")]
    private partial void LogDisconnected(LogLevel level, string player, string error, string delay);

    [LoggerMessage(LogLevel.Information, "Connected to {Player} at {Host}:{Port}, {Version}.")]
    private partial void LogConnected(string player, string host, int port, string version);

    [LoggerMessage(LogLevel.Warning, "{Player} started playing, but reading what it plays failed. {Error}")]
    private partial void LogReadPlayingFailed(string player, string error);

    [LoggerMessage(LogLevel.Debug, "{Player} reported playback with no active video player.")]
    private partial void LogNoActiveVideoPlayer(string player);

    [LoggerMessage(LogLevel.Debug, "{Player} reported playback of an item with no file.")]
    private partial void LogItemWithNoFile(string player);
}
