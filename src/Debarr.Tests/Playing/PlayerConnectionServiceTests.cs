using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.EventStore;
using Debarr.Playing;
using Debarr.Tests.Extensions;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Logging;

namespace Debarr.Tests.Playing;

public sealed class PlayerConnectionServiceTests : IAsyncLifetime
{
    private readonly FakeKodiServer _theater = new();
    private readonly FakeKodiServer _bedroom = new();
    private readonly ConcurrentQueue<PlayerConnectionStateChangedEvent> _stateChanges = new();
    private readonly FakeLoggerProvider _logs = new();
    private TestHost _host = null!;
    private PlayerConnectionService _service = null!;
    private FailingSessionFactory _sessionFactory = null!;
    private IDisposable? _subscription;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(logging: logging => logging.AddProvider(_logs).AddFilter<FakeLoggerProvider>("Debarr", LogLevel.Debug));
        _sessionFactory = new FailingSessionFactory(_host.Store);
        _service = new PlayerConnectionService(
            _sessionFactory,
            _host.Services.GetRequiredService<ReadModelChangeListener>(),
            _host.Services.GetRequiredService<PlayerConnectionFactory>(),
            _host.Services.GetRequiredService<ILogger<PlayerConnectionService>>());
        _subscription = _service.ActivityEvents.OfType<PlayerConnectionStateChangedEvent>().Subscribe(_stateChanges.Enqueue);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        _subscription?.Dispose();
        await _host.DisposeAsync();
        await _theater.DisposeAsync();
        await _bedroom.DisposeAsync();
    }

    [Fact]
    public async Task Startup_connects_each_enabled_player_and_publishes_its_states()
    {
        var theaterId = await SavePlayerAsync("Theater", _theater, enabled: true);
        await SavePlayerAsync("Bedroom", _bedroom, enabled: false);

        await _service.StartAsync(CancellationToken);

        await Poll.UntilAsync(() => _stateChanges.Any(change => change.State is PlayerConnectionState.Connected));
        Assert.Equal(1, _theater.ConnectionCount);
        Assert.Equal(0, _bedroom.ConnectionCount);
        Assert.Collection(
            _stateChanges,
            change => Assert.Equal((theaterId, "Theater", true), (change.PlayerId, change.PlayerName, change.State is PlayerConnectionState.Connecting)),
            change => Assert.Equal((theaterId, "Theater", "Kodi 21.2 (JSON-RPC 13.5.0)"), (change.PlayerId, change.PlayerName, ((PlayerConnectionState.Connected)change.State).Version)));
    }

    [Fact]
    public async Task Saving_a_player_reconnects_that_player_alone_and_its_playback_still_arrives()
    {
        _theater.Respond("Player.GetItem", """{"item":{"type":"movie","title":"Arrival","file":"/media/movies/Arrival.mkv"}}""");
        var theaterId = await SavePlayerAsync("Theater", _theater, enabled: true);
        await SavePlayerAsync("Bedroom", _bedroom, enabled: true);
        await _service.StartAsync(CancellationToken);
        await Poll.UntilAsync(() => _stateChanges.Count(change => change.State is PlayerConnectionState.Connected) == 2);

        await SavePlayerAsync("Theater", _theater, enabled: true, theaterId);

        await Poll.UntilAsync(() => ConnectedCount(theaterId) == 2);
        Assert.Equal(2, _theater.ConnectionCount);
        Assert.Equal(1, _bedroom.ConnectionCount);
        var playbackStarted = _service.PlaybackStarted.FirstAsync().ToTask(CancellationToken);
        await _theater.SendRawAsync("""{"jsonrpc":"2.0","method":"Player.OnPlay","params":{"data":{"item":{"type":"movie"},"player":{"playerid":1}},"sender":"xbmc"}}""");
        Assert.Equal(new PlayerPath("/media/movies/Arrival.mkv"), (await playbackStarted.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken)).PlayerPath);
    }

    [Fact]
    public async Task Disabling_a_player_closes_its_connection_enabling_it_opens_one_and_deleting_it_closes_it()
    {
        var theaterId = await SavePlayerAsync("Theater", _theater, enabled: true);
        await _service.StartAsync(CancellationToken);
        await _theater.WaitForConnectionAsync();

        await SavePlayerAsync("Theater", _theater, enabled: false, theaterId);
        await _theater.WaitForDisconnectionAsync();

        await SavePlayerAsync("Theater", _theater, enabled: true, theaterId);
        await _theater.WaitForConnectionAsync();
        await Poll.UntilAsync(() => ConnectingCount(theaterId) >= 2);
        Assert.Equal(2, ConnectingCount(theaterId));

        await _host.Runtime.SendCommandAsync(new RemovePlayer(theaterId), CancellationToken);
        await _theater.WaitForDisconnectionAsync();
        Assert.Equal(2, _theater.ConnectionCount);
    }

    [Fact]
    public async Task A_player_change_committed_outside_a_command_reconnects_that_player()
    {
        var theaterId = await SavePlayerAsync("Theater", _theater, enabled: true);
        await _service.StartAsync(CancellationToken);
        await Poll.UntilAsync(() => ConnectedCount(theaterId) == 1);

        await using (var session = _host.Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Players.StreamId, new PlayerChanged(theaterId, true, KodiEndpoint.Create("127.0.0.1", _theater.Port, 60, 10).Value, []));
            await session.SaveChangesAsync(CancellationToken);
        }

        await Poll.UntilAsync(() => ConnectedCount(theaterId) == 2);
        Assert.Equal(2, _theater.ConnectionCount);
    }

    [Fact]
    public async Task A_failed_read_leaves_the_player_closed_and_a_later_save_reconnects_it()
    {
        var theaterId = await SavePlayerAsync("Theater", _theater, enabled: true);
        await _service.StartAsync(CancellationToken);
        await Poll.UntilAsync(() => ConnectedCount(theaterId) == 1);

        _sessionFactory.Failing = true;
        await SavePlayerAsync("Theater", _theater, enabled: true, theaterId);
        await _theater.WaitForDisconnectionAsync();

        _sessionFactory.Failing = false;
        await SavePlayerAsync("Theater", _theater, enabled: true, theaterId);
        await Poll.UntilAsync(() => ConnectedCount(theaterId) == 2);
        Assert.Equal(2, ConnectingCount(theaterId));
        Assert.Equal(2, _theater.ConnectionCount);
    }

    [Fact]
    public async Task Adding_a_player_after_startup_connects_it()
    {
        await _service.StartAsync(CancellationToken);

        var bedroomId = await SavePlayerAsync("Bedroom", _bedroom, enabled: true);

        await Poll.UntilAsync(() => ConnectedCount(bedroomId) == 1);
        Assert.Equal(1, _bedroom.ConnectionCount);
    }

    [Fact]
    public async Task What_a_connection_logs_carries_no_scope_of_the_command_that_saved_its_player()
    {
        await _service.StartAsync(CancellationToken);

        await SavePlayerAsync("Bedroom", ClosedPort(), enabled: true);

        await Poll.UntilAsync(() => _logs.Collector.GetSnapshot().Any(IsDisconnectedWarning));
        var disconnected = Assert.Single(_logs.Collector.GetSnapshot(), IsDisconnectedWarning);
        Assert.StartsWith("Bedroom disconnected. Connecting to 127.0.0.1:", disconnected.Message);
        Assert.DoesNotContain(disconnected.Scopes, scope => scope?.ToString()?.StartsWith(nameof(SavePlayer), StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_subscriber_after_startup_receives_the_playback_started_events_of_a_connection_opened_before_it()
    {
        _bedroom.Respond("Player.GetItem", """{"item":{"type":"movie","title":"Arrival","year":2016,"file":"/media/movies/Arrival.mkv"}}""");
        var bedroomId = await SavePlayerAsync("Bedroom", _bedroom, enabled: true);
        await _service.StartAsync(CancellationToken);
        await Poll.UntilAsync(() => _stateChanges.Any(change => change.State is PlayerConnectionState.Connected));

        var playbackStarted = _service.PlaybackStarted.FirstAsync().ToTask(CancellationToken);
        await _bedroom.SendRawAsync("""{"jsonrpc":"2.0","method":"Player.OnPlay","params":{"data":{"item":{"type":"movie"},"player":{"playerid":1}},"sender":"xbmc"}}""");

        var started = await playbackStarted.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        Assert.Equal((bedroomId, "Bedroom", new PlayerPath("/media/movies/Arrival.mkv"), "Arrival (2016)"), (started.PlayerId, started.PlayerName, started.PlayerPath, started.Title));
    }

    [Fact]
    public async Task Connection_states_start_empty_and_replay_each_players_latest_state_to_a_later_subscriber()
    {
        await _service.StartAsync(CancellationToken);
        Assert.Empty(await _service.ConnectionStates.FirstAsync().ToTask(CancellationToken));

        var theaterId = await SavePlayerAsync("Theater", _theater, enabled: true);
        var bedroomId = await SavePlayerAsync("Bedroom", _bedroom, enabled: true);
        await Poll.UntilAsync(() => ConnectedCount(theaterId) == 1 && ConnectedCount(bedroomId) == 1);

        var states = await _service.ConnectionStates.FirstAsync().ToTask(CancellationToken);
        Assert.Equal(
            [(bedroomId, "Bedroom", true), (theaterId, "Theater", true)],
            states.Values.OrderBy(state => state.PlayerName, StringComparer.Ordinal).Select(state => (state.PlayerId, state.PlayerName, state.State is PlayerConnectionState.Connected)));
    }

    [Fact]
    public async Task Stopping_closes_every_connection()
    {
        await SavePlayerAsync("Theater", _theater, enabled: true);
        await SavePlayerAsync("Bedroom", _bedroom, enabled: true);
        await _service.StartAsync(CancellationToken);
        await _theater.WaitForConnectionAsync();
        await _bedroom.WaitForConnectionAsync();

        await _service.StopAsync(CancellationToken);

        await _theater.WaitForDisconnectionAsync();
        await _bedroom.WaitForDisconnectionAsync();
    }

    private int ConnectedCount(Guid playerId) =>
        _stateChanges.Count(change => change.PlayerId == playerId && change.State is PlayerConnectionState.Connected);

    private int ConnectingCount(Guid playerId) =>
        _stateChanges.Count(change => change.PlayerId == playerId && change.State is PlayerConnectionState.Connecting);

    private static bool IsDisconnectedWarning(FakeLogRecord log) =>
        log.Category == typeof(KodiPlayerConnection).FullName && log.Level == LogLevel.Warning;

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private Task<Guid> SavePlayerAsync(string name, FakeKodiServer server, bool enabled, Guid? id = null) =>
        SavePlayerAsync(name, server.Port, enabled, id);

    private async Task<Guid> SavePlayerAsync(string name, int port, bool enabled, Guid? id = null)
    {
        var command = new SavePlayer(id ?? Guid.NewGuid(), name, enabled, KodiEndpoint.Create("127.0.0.1", port, 60, 10).Value, []);
        Assert.True((await _host.Runtime.SendCommandAsync(command, CancellationToken)).IsSuccess);
        return command.PlayerId;
    }

    /// <summary>Opens the store's sessions, except that opening a query session throws while <see cref="Failing"/> is set.</summary>
    private sealed class FailingSessionFactory(IDocumentStore store) : ISessionFactory
    {
        public volatile bool Failing;

        public IQuerySession QuerySession() =>
            Failing ? throw new InvalidOperationException("The database is unavailable.") : store.QuerySession();

        public IDocumentSession OpenSession() => store.LightweightSession();
    }
}
