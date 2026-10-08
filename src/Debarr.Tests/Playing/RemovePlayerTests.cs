using Debarr.Playing;

namespace Debarr.Tests.Playing;

public sealed class RemovePlayerTests
{
    private static readonly Guid PlayerId = Guid.NewGuid();

    private static readonly Players Stored = Players.Create(new PlayerAdded(PlayerId, "Theater", true, KodiEndpoint.Create("kodi.lan", 9090, 60, 10).Value, [], []));

    [Fact]
    public void A_player_is_removed()
    {
        Assert.Equal([new PlayerRemoved(PlayerId)], RemovePlayerHandler.Handle(new RemovePlayer(PlayerId), Stored));
    }

    [Fact]
    public void Removing_a_removed_or_missing_player_does_nothing()
    {
        Assert.Empty(RemovePlayerHandler.Handle(new RemovePlayer(PlayerId), Stored.Apply(new PlayerRemoved(PlayerId))));
        Assert.Empty(RemovePlayerHandler.Handle(new RemovePlayer(Guid.NewGuid()), Stored));
        Assert.Empty(RemovePlayerHandler.Handle(new RemovePlayer(PlayerId), null));
    }
}
