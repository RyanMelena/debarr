using Debarr.Notifying;

namespace Debarr.Tests.Notifying;

public sealed class RemoveNotifierTests
{
    private static readonly Guid NotifierId = Guid.NewGuid();

    private static readonly Notifiers Stored =
        Notifiers.Create(new NotifierAdded(NotifierId, "broker", true, MqttSettings.Create("mqtt://broker.lan", null, null, null, "debarr/player/{player}/playback", QualityOfService.AtLeastOnce).Value));

    [Fact]
    public void A_notifier_is_removed()
    {
        Assert.Equal([new NotifierRemoved(NotifierId)], RemoveNotifierHandler.Handle(new RemoveNotifier(NotifierId), Stored));
    }

    [Fact]
    public void Removing_a_removed_or_missing_notifier_does_nothing()
    {
        Assert.Empty(RemoveNotifierHandler.Handle(new RemoveNotifier(NotifierId), Stored.Apply(new NotifierRemoved(NotifierId))));
        Assert.Empty(RemoveNotifierHandler.Handle(new RemoveNotifier(Guid.NewGuid()), Stored));
        Assert.Empty(RemoveNotifierHandler.Handle(new RemoveNotifier(NotifierId), null));
    }
}
