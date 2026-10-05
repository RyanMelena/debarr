using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Debarr.Activity;
using Debarr.EventStore;
using Fisher;

namespace Debarr.Playing;

/// <summary>
/// Holds a player connection for each enabled player, from startup to shutdown.
/// Each committed change to a player replaces that player's connection alone, or closes it when the player is disabled or removed.
/// </summary>
public sealed partial class PlayerConnectionService : IHostedService, IActivitySource, IDisposable
{
    private readonly ISessionFactory _sessionFactory;

    private readonly PlayerConnectionFactory _playerConnectionFactory;

    private readonly ILogger<PlayerConnectionService> _logger;

    private readonly IConnectableObservable<PlayerConnectionEvent> _events;

    private readonly IConnectableObservable<ImmutableDictionary<Guid, PlayerConnectionStateChangedEvent>> _connectionStates;

    private readonly CompositeDisposable _disposables = [];

    // The ids of the enabled players as startup read them.
    private IReadOnlyList<Guid> _startupPlayerIds = [];

    public PlayerConnectionService(
        ISessionFactory sessionFactory,
        ReadModelChangeListener readModelChangeListener,
        PlayerConnectionFactory playerConnectionFactory,
        ILogger<PlayerConnectionService> logger)
    {
        _sessionFactory = sessionFactory;
        _playerConnectionFactory = playerConnectionFactory;
        _logger = logger;

        _events = Observable
            .Merge(
                Observable.Defer(() => _startupPlayerIds.ToObservable()),
                readModelChangeListener.Committed<PlayerAdded>().Select(added => added.PlayerId),
                readModelChangeListener.Committed<PlayerChanged>().Select(changed => changed.PlayerId),
                readModelChangeListener.Committed<PlayerRenamed>().Select(renamed => renamed.PlayerId),
                readModelChangeListener.Committed<PlayerRemoved>().Select(removed => removed.PlayerId))
            .GroupBy(playerId => playerId)
            .SelectMany(CurrentPlayerConnectionEvents)
            .Publish();

        _connectionStates = _events
            .OfType<PlayerConnectionStateChangedEvent>()
            .Scan(ImmutableDictionary<Guid, PlayerConnectionStateChangedEvent>.Empty, (states, stateChanged) => states.SetItem(stateChanged.PlayerId, stateChanged))
            .StartWith(ImmutableDictionary<Guid, PlayerConnectionStateChangedEvent>.Empty)
            .Replay(1);
    }

    /// <summary>The playback started events of every open player connection.</summary>
    public IObservable<PlaybackStartedEvent> PlaybackStarted => _events.OfType<PlaybackStartedEvent>();

    /// <summary>A <see cref="PlayerConnectionStateChangedEvent"/> for each state each player connection enters.</summary>
    public IObservable<ActivityEvent> ActivityEvents => _events.OfType<PlayerConnectionStateChangedEvent>();

    /// <summary>
    /// The latest state change of each player connection opened since startup, keyed by player id, replayed at once to each subscriber.
    /// A player whose connection is closed keeps the last state it entered.
    /// </summary>
    public IObservable<IReadOnlyDictionary<Guid, PlayerConnectionStateChangedEvent>> ConnectionStates => _connectionStates;

    /// <summary>Reads the ids of the enabled players, before the host accepts requests, and opens a connection for each.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var session = _sessionFactory.QuerySession())
        {
            _startupPlayerIds = [.. (await Players.ReadAsync(session, cancellationToken)).All.Where(player => player.Enabled).Select(player => player.Id)];
        }

        _connectionStates.Connect().DisposeWith(_disposables);
        _events.Connect().DisposeWith(_disposables);
    }

    /// <summary>Closes every connection.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposables.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _disposables.Dispose();

    /// <summary>
    /// The events of one player's current connection.
    /// <paramref name="changes"/> holds that player's id once at startup when it is enabled, and again after each commit that changes it.
    /// Each change reads the player and makes a new connection current when it is enabled, or none when it is disabled, removed or unreadable.
    /// A newer change cancels the read or closes the connection that came before it.
    /// </summary>
    private IObservable<PlayerConnectionEvent> CurrentPlayerConnectionEvents(IGroupedObservable<Guid, Guid> changes) =>
        changes
            .Select(playerId => Observable.FromAsync(cancellationToken => GetPlayerAsync(playerId, cancellationToken))
                .SelectMany(player => player is { Enabled: true }
                    ? _playerConnectionFactory.Create(player)
                    : Observable.Empty<PlayerConnectionEvent>()))
            .Switch();

    /// <summary>The player, or null when it is removed or the read fails.</summary>
    private async Task<Player?> GetPlayerAsync(Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            await using var session = _sessionFactory.QuerySession();
            return (await Players.ReadAsync(session, cancellationToken)).Find(playerId);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogPlayerUnread(exception, playerId);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not read player {PlayerId}. Its connection stays closed until it changes again.")]
    private partial void LogPlayerUnread(Exception exception, Guid playerId);
}
