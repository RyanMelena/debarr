using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>Asks before an action that deletes, clears or discards something, with the action's name on its red button.</summary>
public partial class ConfirmDialog
{
    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = default!;

    [Parameter, EditorRequired]
    public string Message { get; set; } = "";

    [Parameter, EditorRequired]
    public string ConfirmText { get; set; } = "";

    private void Cancel() => Dialog.Cancel();

    private void Accept() => Dialog.Close(DialogResult.Ok(true));
}
