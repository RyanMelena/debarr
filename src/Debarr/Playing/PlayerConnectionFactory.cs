using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using FluentResults;

namespace Debarr.Playing;

/// <summary>Opens the connection to a player for its type.</summary>
public sealed class PlayerConnectionFactory(TimeProvider timeProvider, ILogger<KodiPlayerConnection> kodiLogger)
{
    /// <summary>
    /// Debarr's live link to the player.
    /// Each subscription is one connection, which connects, reconnects with backoff, and closes when the subscription is disposed.
    /// It emits a <see cref="PlayerConnectionStateChangedEvent"/> for each state it enters and a <see cref="PlaybackStartedEvent"/> for each file the player starts.
    /// </summary>
    public IObservable<PlayerConnectionEvent> Create(Player player) => player.Endpoint switch
    {
        KodiEndpoint kodi => new KodiPlayerConnection(player, kodi, timeProvider, kodiLogger).Events,
        _ => throw new NotSupportedException($"The player endpoint {player.Endpoint.GetType().Name} has no connection."),
    };

    /// <summary>Opens one connection and returns the player's version once it connects, or the error of the attempt that fails, then closes the connection.</summary>
    public async Task<Result<string>> TestAsync(Player player, CancellationToken cancellationToken)
    {
        var state = await Create(player)
            .OfType<PlayerConnectionStateChangedEvent>()
            .Select(stateChanged => stateChanged.State)
            .FirstAsync(state => state is not PlayerConnectionState.Connecting)
            .ToTask(cancellationToken);
        return state is PlayerConnectionState.Connected connected
            ? Result.Ok(connected.Version)
            : Result.Fail(((PlayerConnectionState.Disconnected)state).Error);
    }
}
