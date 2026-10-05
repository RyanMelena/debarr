using Debarr.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Debarr.Components;

/// <summary>
/// Saves a form, and is enabled only while the form has unsaved changes.
/// While it has them, leaving for another page asks first, and closing or reloading the tab asks through the browser.
/// </summary>
public partial class SaveButton(IDialogService dialogService, NavigationManager navigation)
{
    [Parameter, EditorRequired]
    public bool HasUnsavedChanges { get; set; }

    [Parameter]
    public bool Saving { get; set; }

    /// <summary>Holds the button disabled while the form has unsaved changes, such as while another action on the page runs.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback OnSave { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Asks before a navigation to another page discards the unsaved changes; a link within the page leaves them in place.</summary>
    private async Task ConfirmLeavingAsync(LocationChangingContext context)
    {
        if (!HasUnsavedChanges || IsSamePage(context.TargetLocation))
        {
            return;
        }

        var leave = await dialogService.ConfirmAsync(
            "Unsaved Changes",
            "This page has changes that are not saved. Leave it and discard them?",
            "Discard Changes");
        if (!leave)
        {
            context.PreventNavigation();
        }
    }

    private bool IsSamePage(string targetLocation) =>
        navigation.ToAbsoluteUri(targetLocation).GetLeftPart(UriPartial.Path) == new Uri(navigation.Uri).GetLeftPart(UriPartial.Path);
}
