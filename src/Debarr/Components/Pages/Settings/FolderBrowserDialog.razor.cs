using Debarr.Scanning;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components.Pages.Settings;

/// <summary>Browses the folders on Debarr's filesystem, and closes with the path of the folder the operator chooses.</summary>
public partial class FolderBrowserDialog
{
    private FolderListing _listing = new(null, []);

    private string _typedPath = "";

    private string? _error;

    [CascadingParameter]
    private IMudDialogInstance Dialog { get; set; } = default!;

    /// <summary>The path typed in the field that opened the dialog, which it starts at when that is a full path to a folder that exists.</summary>
    [Parameter]
    public string InitialPath { get; set; } = "";

    /// <summary>The folder listed, while the field names it.</summary>
    private LocalPath? ChosenFolder => _error is null ? _listing.Folder : null;

    protected override void OnInitialized()
    {
        var initialPath = InitialPath.Trim();
        if (Path.IsPathFullyQualified(initialPath) && FolderListing.Read(new LocalPath(initialPath)) is { IsSuccess: true } initial)
        {
            Take(initial.Value);
        }
        else
        {
            Show(null);
        }
    }

    /// <summary>Lists the folder, or the top for null, and puts its path in the field.</summary>
    private void Show(LocalPath? folder)
    {
        var listing = FolderListing.Read(folder);
        if (listing.IsSuccess)
        {
            Take(listing.Value);
        }
        else
        {
            _typedPath = folder?.Value ?? "";
            _error = listing.Errors[0].Message;
        }
    }

    private void Take(FolderListing listing)
    {
        _listing = listing;
        _typedPath = listing.Folder?.Value ?? "";
        _error = null;
    }

    /// <summary>Lists the typed folder, or the top for empty text, and keeps the listing with the reason beneath the field when the typed path names no folder Debarr can read.</summary>
    private void SetTypedPath(string typedPath)
    {
        _typedPath = typedPath;
        var trimmed = typedPath.Trim();
        if (trimmed.Length > 0 && !Path.IsPathFullyQualified(trimmed))
        {
            _error = "Enter a full path.";
            return;
        }

        var listing = FolderListing.Read(trimmed.Length == 0 ? null : new LocalPath(trimmed));
        if (listing.IsSuccess)
        {
            _listing = listing.Value;
            _error = null;
        }
        else
        {
            _error = listing.Errors[0].Message;
        }
    }

    /// <summary>The folder's own name, or the whole path for a drive.</summary>
    private static string NameOf(LocalPath folder) => Path.GetFileName(folder.Value) is { Length: > 0 } name ? name : folder.Value;

    private void Cancel() => Dialog.Cancel();

    private void Choose()
    {
        if (ChosenFolder is { } folder)
        {
            Dialog.Close(DialogResult.Ok(folder.Value));
        }
    }
}
