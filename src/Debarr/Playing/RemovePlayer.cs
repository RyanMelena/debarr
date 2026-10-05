using Wolverine.Persistence.EventSourcing;

namespace Debarr.Playing;

public sealed record RemovePlayer(Guid PlayerId)
{
    /// <summary>The players' stream, which Wolverine loads the aggregate from.</summary>
    public Guid PlayersId => Players.StreamId;
}

public static class RemovePlayerHandler
{
    /// <summary>Removes the player, and does nothing when it is already gone.</summary>
    public static IReadOnlyList<object> Handle(RemovePlayer command, [WriteModel(Required = false)] Players? players) =>
        players?.Find(command.PlayerId) is null ? [] : [new PlayerRemoved(command.PlayerId)];
}
