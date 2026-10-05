using FluentResults;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Notifying;

/// <param name="NotifierId">The notifier's id; a new notifier's id comes from its form.</param>
public sealed record SaveNotifier(Guid NotifierId, string Name, bool Enabled, NotifierSettings Settings)
{
    /// <summary>The notifiers' stream, which Wolverine loads the aggregate from.</summary>
    public Guid NotifiersId => Notifiers.StreamId;
}

public static class SaveNotifierHandler
{
    /// <summary>
    /// Refuses a save of a notifier that was removed, or that changes its type,
    /// and refuses beneath its field a blank name, one another notifier has, and a setting outside its bounds.
    /// </summary>
    public static Result Validate(SaveNotifier command, Notifiers? notifiers)
    {
        notifiers ??= Notifiers.Empty;
        if (notifiers.WasRemoved(command.NotifierId))
        {
            return Result.Fail($"The notifier {command.Name} was removed.");
        }

        if (notifiers.Find(command.NotifierId) is { } notifier && notifier.Settings.GetType() != command.Settings.GetType())
        {
            return Result.Fail($"The notifier {notifier.Name} keeps its type.");
        }

        List<IError> errors = [.. ValidateName(command.NotifierId, command.Name, notifiers), .. command.Settings.Validate().Errors];
        return errors.Count > 0 ? Result.Fail(errors) : Result.Ok();
    }

    /// <summary>Refuses a blank name or one another notifier has.</summary>
    public static IEnumerable<FieldError> ValidateName(Guid notifierId, string name, Notifiers notifiers)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            yield return new FieldError(nameof(SaveNotifier.Name), "Enter a name.");
        }
        else if (notifiers.All.Any(other => other.Id != notifierId && string.Equals(other.Name, name, StringComparison.Ordinal)))
        {
            yield return new FieldError(nameof(SaveNotifier.Name), $"A notifier named {name} already exists.");
        }
    }

    /// <summary>Adds a new notifier, and otherwise changes its settings, after renaming it when its name changed.</summary>
    public static IReadOnlyList<object> Handle(SaveNotifier command, [WriteModel(Required = false)] Notifiers? notifiers) =>
        (notifiers ?? Notifiers.Empty).Find(command.NotifierId) switch
        {
            null => [new NotifierAdded(command.NotifierId, command.Name, command.Enabled, command.Settings)],
            { Name: var name } when name == command.Name => [new NotifierChanged(command.NotifierId, command.Enabled, command.Settings)],
            _ =>
            [
                new NotifierRenamed(command.NotifierId, command.Name),
                new NotifierChanged(command.NotifierId, command.Enabled, command.Settings),
            ],
        };
}
