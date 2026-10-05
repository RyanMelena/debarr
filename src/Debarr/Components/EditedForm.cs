using FluentResults;

namespace Debarr.Components;

/// <summary>
/// A form the operator edits against the values last saved.
/// A reload takes the newly saved values and keeps unsaved edits, and a save that succeeds makes the values it sent the saved ones.
/// A reload that finds the saved values changed under unsaved edits, such as by a save in another tab, marks the form out of date, and its save is refused.
/// </summary>
/// <param name="copy">A copy of a form that edits apart from it.</param>
/// <param name="hasChanges">Whether the edited form differs from the saved one; by default, whether the two are unequal.</param>
public sealed class EditedForm<TForm>(Func<TForm, TForm> copy, Func<TForm, TForm, bool>? hasChanges = null)
    where TForm : class
{
    private readonly Func<TForm, TForm, bool> _hasChanges = hasChanges ?? ((edited, saved) => !edited.Equals(saved));

    // The saved values the edits started from.
    private TForm? _editedFrom;

    /// <summary>The values last saved; null until the first reload.</summary>
    public TForm? Saved { get; private set; }

    /// <summary>The values the operator edits; null until the first reload.</summary>
    public TForm? Model { get; private set; }

    public bool HasUnsavedChanges => Model is not null && Saved is not null && _hasChanges(Model, Saved);

    /// <summary>Whether the saved values changed since the unsaved edits started from them.</summary>
    public bool IsOutOfDate => HasUnsavedChanges && _editedFrom is not null && _hasChanges(Saved!, _editedFrom);

    /// <summary>Takes the saved values a reload read, and shows them in place of the edited ones unless those have unsaved changes.</summary>
    public void Take(TForm saved)
    {
        if (!HasUnsavedChanges)
        {
            Model = copy(saved);
            _editedFrom = saved;
        }

        Saved = saved;
    }

    /// <summary>Forgets the values, so the next reload shows the saved ones.</summary>
    public void Clear() => (Saved, Model, _editedFrom) = (null, null, null);

    /// <summary>
    /// Sends a copy of the edited values through <paramref name="save"/>, and makes them the saved values once it succeeds.
    /// A form that is out of date sends nothing, keeps its edits, and returns the refusal.
    /// </summary>
    public async Task<Result> SaveAsync(Func<TForm, Task<Result>> save)
    {
        if (IsOutOfDate)
        {
            return Result.Fail("Saved in another tab. Reload to see the change.");
        }

        var edited = copy(Model ?? throw new InvalidOperationException("The form has no values before its first reload."));
        var saved = await save(edited);
        if (saved.IsSuccess)
        {
            (Saved, _editedFrom) = (edited, edited);
        }

        return saved;
    }
}
