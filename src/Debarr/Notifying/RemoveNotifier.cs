using Wolverine.Persistence.EventSourcing;

namespace Debarr.Notifying;

public sealed record RemoveNotifier(Guid NotifierId)
{
    /// <summary>The notifiers' stream, which Wolverine loads the aggregate from.</summary>
    public Guid NotifiersId => Notifiers.StreamId;
}

public static class RemoveNotifierHandler
{
    /// <summary>Removes the notifier, and does nothing when it is already gone.</summary>
    public static IReadOnlyList<object> Handle(RemoveNotifier command, [WriteModel(Required = false)] Notifiers? notifiers) =>
        notifiers?.Find(command.NotifierId) is null ? [] : [new NotifierRemoved(command.NotifierId)];
}
