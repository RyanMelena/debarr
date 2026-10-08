using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Debarr.Detecting;
using Debarr.Playing;
using Debarr.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Playing;

public sealed class KodiPlayerConnectionTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 20, 15, 0, TimeSpan.Zero);

    private const string ArrivalItem =
        """{"item":{"type":"movie","id":329,"label":"Arrival","title":"Arrival","year":2016,"file":"smb://nas/media/movies/Arrival%20(2016)/Arrival.mkv","streamdetails":{"video":[{"aspect":2.39}]}}}""";

    private const string ArrivalGetItemParameters = """{"playerid":1,"properties":["file","title","showtitle","year","season","episode","streamdetails"]}""";

    private readonly FakeKodiServer _server = new();
    private readonly ConcurrentQueue<PlayerConnectionEvent> _events = new();

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private static readonly Guid PlayerId = Guid.NewGuid();

    private IEnumerable<PlaybackStartedEvent> PlaybackStarted => _events.OfType<PlaybackStartedEvent>();

    private IEnumerable<PlayerConnectionState> States => _events.OfType<PlayerConnectionStateChangedEvent>().Select(changed => changed.State);

    [Fact]
    public async Task An_OnPlay_emits_the_canonical_file_path_the_reported_ratio_and_title_and_the_time_it_arrived()
    {
        _server.Respond("Player.GetItem", ArrivalItem);
        using var connection = await ConnectAsync(new FakeTimeProvider(Now));

        await _server.SendRawAsync(OnPlay("movie", 1));

        await Poll.UntilAsync(() => PlaybackStarted.Any());
        Assert.Equal(
            new PlaybackStartedEvent(PlayerId, "Theater", new PlayerPath("smb://nas/media/movies/Arrival (2016)/Arrival.mkv"), new AspectRatio(2.39), "Arrival (2016)", Now),
            PlaybackStarted.Single());
    }

    [Theory]
    [InlineData(
        """{"item":{"type":"episode","label":"Home","title":"Home","showtitle":"The Expanse","season":2,"episode":5,"file":"/media/tv/e.mkv"}}""",
        "The Expanse S02E05 Home")]
    [InlineData("""{"item":{"type":"movie","label":"Arrival","title":"Arrival","year":0,"file":"/media/m.mkv"}}""", "Arrival")]
    [InlineData("""{"item":{"type":"unknown","label":"Arrival.mkv","title":"","file":"/media/Arrival.mkv"}}""", "Arrival.mkv")]
    [InlineData("""{"item":{"type":"unknown","label":"","file":"/media/Arrival.mkv"}}""", null)]
    public async Task The_title_names_an_episode_by_show_season_and_episode_and_anything_else_by_its_title_or_label(string item, string? title)
    {
        _server.Respond("Player.GetItem", item);
        using var connection = await ConnectAsync();

        await _server.SendRawAsync(OnPlay("movie", 1));

        await Poll.UntilAsync(() => PlaybackStarted.Any());
        Assert.Equal(title, PlaybackStarted.Single().Title);
    }

    [Fact]
    public async Task Notifications_sharing_one_write_are_framed_apart_and_an_OnPlay_without_a_player_reads_the_active_video_player()
    {
        _server.Respond("Player.GetItem", ArrivalItem);
        _server.Respond("Player.GetActivePlayers", """[{"playerid":0,"type":"audio"},{"playerid":1,"type":"video"}]""");
        using var connection = await ConnectAsync();

        await _server.SendRawAsync(
            OnPlay("song", 0)
            + OnPlay("picture", 2)
            + OnPlay("movie", -1)
            + """{"jsonrpc":"2.0","method":"Player.OnPlay","params":{"data":{"item":{"type":"movie"}},"sender":"xbmc"}}""");

        await Poll.UntilAsync(() => PlaybackStarted.Count() == 2);
        Assert.Equal([ArrivalGetItemParameters, ArrivalGetItemParameters], _server.RequestParameters("Player.GetItem"));
    }

    [Fact]
    public async Task An_OnPlay_with_no_active_video_player_emits_nothing()
    {
        _server.Respond("Player.GetItem", ArrivalItem);
        using var connection = await ConnectAsync();

        // The connection reads OnPlays in order, so once the second has emitted, the first has been handled.
        await _server.SendRawAsync(OnPlay("movie", -1));
        await _server.SendRawAsync(OnPlay("movie", 1));

        await Poll.UntilAsync(() => PlaybackStarted.Any());
        Assert.Single(PlaybackStarted);
        Assert.Equal([ArrivalGetItemParameters], _server.RequestParameters("Player.GetItem"));
    }

    [Fact]
    public async Task An_OnPlay_of_an_excluded_path_inside_a_mapped_one_emits_nothing_and_logs_no_path()
    {
        var logger = new FakeLogger<KodiPlayerConnection>();
        var endpoint = KodiEndpoint.Create("127.0.0.1", _server.Port, 60, 5).Value;
        var player = new Player(
            PlayerId,
            "Theater",
            true,
            endpoint,
            [new PathMapping(new PlayerPath("smb://nas/media"), new LocalPath("/media"))],
            [new PlayerPath("smb://nas/media/private")]);
        _server.Respond("Player.GetItem", """{"item":{"type":"movie","label":"Secret","title":"Secret","file":"smb://nas/media/private/Secret.mkv"}}""");
        using var subscription = new KodiPlayerConnection(player, endpoint, TimeProvider.System, logger).Events.Subscribe(_events.Enqueue);
        await Poll.UntilAsync(() => States.OfType<PlayerConnectionState.Connected>().Any());

        await _server.SendRawAsync(OnPlay("movie", 1));
        await Poll.UntilAsync(() => logger.Collector.GetSnapshot().Any(record => record.Message == "Theater played an excluded path, so its playback was ignored."));
        _server.Respond("Player.GetItem", ArrivalItem);
        await _server.SendRawAsync(OnPlay("movie", 1));

        await Poll.UntilAsync(() => PlaybackStarted.Any());
        Assert.Equal(new PlayerPath("smb://nas/media/movies/Arrival (2016)/Arrival.mkv"), Assert.Single(PlaybackStarted).PlayerPath);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Message.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dropped_connection_reports_disconnected_while_it_retries_and_reconnects()
    {
        using var connection = await ConnectAsync();

        _server.Drop();

        await Poll.UntilAsync(() => States.Count() == 5);
        Assert.Collection(
            States,
            state => Assert.IsType<PlayerConnectionState.Connecting>(state),
            state => Assert.Equal("Kodi 21.2 (JSON-RPC 13.5.0)", Assert.IsType<PlayerConnectionState.Connected>(state).Version),
            state => Assert.Equal("The connection to Kodi ended.", Assert.IsType<PlayerConnectionState.Disconnected>(state).Error),
            state => Assert.Equal(("The connection to Kodi ended.", null), (Assert.IsType<PlayerConnectionState.Disconnected>(state).Error, ((PlayerConnectionState.Disconnected)state).RetryAt)),
            state => Assert.IsType<PlayerConnectionState.Connected>(state));
    }

    [Fact]
    public async Task A_player_that_never_connects_stays_disconnected_since_its_first_failure_and_says_when_it_retries()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var connection = Connection(
            "Bedroom",
            KodiEndpoint.Create("127.0.0.1", closedPort, 60, 10).Value,
            TimeProvider.System,
            NullLogger<KodiPlayerConnection>.Instance);

        using var subscription = connection.Events.Subscribe(_events.Enqueue);
        await Poll.UntilAsync(() => States.OfType<PlayerConnectionState.Disconnected>().Count(state => state.RetryAt is not null) >= 2, TimeSpan.FromSeconds(10));

        var disconnected = States.OfType<PlayerConnectionState.Disconnected>().Take(3).ToList();
        Assert.Single(disconnected.Select(state => state.Since).Distinct());
        Assert.Equal([true, false, true], disconnected.Select(state => state.RetryAt is not null));
        Assert.All(disconnected, state => Assert.StartsWith($"Connecting to 127.0.0.1:{closedPort} failed: ", state.Error));
    }

    [Fact]
    public async Task A_player_that_stays_off_warns_once_and_logs_each_failed_retry_at_debug()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var logger = new FakeLogger<KodiPlayerConnection>();
        var connection = Connection(
            "Bedroom",
            KodiEndpoint.Create("127.0.0.1", closedPort, 60, 10).Value,
            TimeProvider.System,
            logger);

        using var subscription = connection.Events.Subscribe(_events.Enqueue);
        await Poll.UntilAsync(() => logger.Collector.GetSnapshot().Count(record => record.Message.StartsWith("Bedroom disconnected. ", StringComparison.Ordinal)) >= 2, TimeSpan.FromSeconds(10));

        var disconnections = logger.Collector.GetSnapshot().Where(record => record.Message.StartsWith("Bedroom disconnected. ", StringComparison.Ordinal)).Take(2).ToList();
        Assert.Equal([LogLevel.Warning, LogLevel.Debug], disconnections.Select(record => record.Level));
        Assert.EndsWith("Retrying in 1 s.", disconnections[0].Message);
        Assert.EndsWith("Retrying in 2 s.", disconnections[1].Message);
    }

    [Fact]
    public async Task An_unanswered_ping_disconnects()
    {
        _server.Hold("JSONRPC.Ping");
        using var connection = await ConnectAsync(pingIntervalSeconds: 1, requestTimeoutSeconds: 1);

        await Poll.UntilAsync(() => States.OfType<PlayerConnectionState.Disconnected>().Any());
        Assert.Equal("JSONRPC.Ping timed out after 1 s.", States.OfType<PlayerConnectionState.Disconnected>().First().Error);
    }

    [Fact]
    public async Task The_version_falls_back_to_Kodi_and_zero_parts_when_fields_are_missing()
    {
        _server.Respond("JSONRPC.Version", """{"version":{"major":12}}""");
        _server.Respond("Application.GetProperties", "{}");

        using var connection = await ConnectAsync();

        Assert.Equal("Kodi (JSON-RPC 12.0.0)", States.OfType<PlayerConnectionState.Connected>().Single().Version);
    }

    [Fact]
    public async Task Disposing_the_subscription_closes_the_socket()
    {
        var connection = await ConnectAsync();

        connection.Dispose();

        await _server.WaitForDisconnectionAsync();
    }

    private static string OnPlay(string type, int playerId) =>
        $$$"""{"jsonrpc":"2.0","method":"Player.OnPlay","params":{"data":{"item":{"type":"{{{type}}}"},"player":{"playerid":{{{playerId}}},"speed":1}},"sender":"xbmc"}}""";

    /// <summary>Subscribes, which opens a connection, and waits until it is connected, so notifications sent next reach it.</summary>
    private static KodiPlayerConnection Connection(string name, KodiEndpoint endpoint, TimeProvider timeProvider, ILogger<KodiPlayerConnection> logger) =>
        new(new Player(PlayerId, name, true, endpoint, [], []), endpoint, timeProvider, logger);

    private async Task<IDisposable> ConnectAsync(TimeProvider? timeProvider = null, int pingIntervalSeconds = 60, int requestTimeoutSeconds = 5)
    {
        var connection = Connection(
            "Theater",
            KodiEndpoint.Create("127.0.0.1", _server.Port, pingIntervalSeconds, requestTimeoutSeconds).Value,
            timeProvider ?? TimeProvider.System,
            NullLogger<KodiPlayerConnection>.Instance);
        var subscription = connection.Events.Subscribe(_events.Enqueue);
        await Poll.UntilAsync(() => States.OfType<PlayerConnectionState.Connected>().Any());
        return subscription;
    }
}
