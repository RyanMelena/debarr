namespace Debarr.Components;

/// <summary>Whether settings pages and modals show the settings an operator rarely changes, and the action that flips it.</summary>
/// <remarks>The layout cascades a new instance on each flip, so every page and open modal renders again.</remarks>
public sealed record ShowAdvanced(bool Shown, Action Toggle)
{
    /// <summary>Turns Show Advanced on when it hides a field the action's last run refused, so that field shows its error.</summary>
    public void RevealRefused(PageAction action, IEnumerable<string> advancedFields)
    {
        if (!Shown && advancedFields.Any(field => action.FieldError(field) is not null))
        {
            Toggle();
        }
    }
}
