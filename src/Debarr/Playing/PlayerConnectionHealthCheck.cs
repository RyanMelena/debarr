using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Health;
using Fisher;

namespace Debarr.Playing;

/// <summary>Finds each enabled player whose connection is disconnected.</summary>
public sealed class PlayerConnectionHealthCheck(
    IDocumentStore store,
    PlayerConnectionService playerConnectionService,
    ReadModelChangeListener readModelChangeListener) : IHealthCheck
{
    public HealthCheckKind Kind => HealthCheckKind.PlayerConnection;

    public IObservable<Unit> Triggers { get; } = playerConnectionService.ConnectionStates
        .Select(_ => Unit.Default)
        .Merge(readModelChangeListener.ChangesTo(nameof(Players)).Select(_ => Unit.Default));

    public async Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken)
    {
        var connectionStates = await playerConnectionService.ConnectionStates.FirstAsync().ToTask(cancellationToken);
        await using var session = store.QuerySession();
        var players = (await Players.ReadAsync(session, cancellationToken)).All
            .Where(player => player.Enabled)
            .OrderBy(player => player.Name, StringComparer.Ordinal);

        return
        [
            .. players
                .Select(player => (player.Name, connectionStates.GetValueOrDefault(player.Id)?.State))
                .Where(player => player.State is PlayerConnectionState.Disconnected)
                .Select(player => (player.Name, State: (PlayerConnectionState.Disconnected)player.State!))
                .Select(player => new HealthMessage(
                    HealthSeverity.Warning,
                    $"{player.Name} is disconnected, so its playback sends nothing: {player.State.Error}",
                    "settings/players",
                    "Fix in Settings > Players",
                    player.State.Since)),
        ];
    }
}
