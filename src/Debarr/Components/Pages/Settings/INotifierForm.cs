using Debarr.Notifying;
using FluentResults;

namespace Debarr.Components.Pages.Settings;

/// <summary>A notifier as its type's modal edits it.</summary>
public interface INotifierForm
{
    Guid Id { get; }

    string Name { get; }

    Result<SaveNotifier> ToSaveNotifier(Notifiers notifiers);
}
