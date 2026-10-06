using Debarr.Components;
using Debarr.Components.Pages.Settings;
using MudBlazor;

namespace Debarr.Extensions;

public static class IDialogServiceExtensions
{
    private static readonly DialogOptions ConfirmOptions = new() { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };

    private static readonly DialogOptions FolderBrowserOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true };

    /// <summary>Shows a <see cref="ConfirmDialog"/>, and returns true when the operator presses <paramref name="confirmText"/>.</summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogService, string title, string message, string confirmText)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { dialog => dialog.Message, message },
            { dialog => dialog.ConfirmText, confirmText },
        };
        var dialog = await dialogService.ShowAsync<ConfirmDialog>(title, parameters, ConfirmOptions);
        var result = await dialog.Result;
        return result is { Canceled: false };
    }

    /// <summary>Shows a <see cref="FolderBrowserDialog"/> from <paramref name="initialPath"/>, and returns the path of the folder the operator chooses, or null when they cancel.</summary>
    public static async Task<string?> ChooseFolderAsync(this IDialogService dialogService, string initialPath)
    {
        var parameters = new DialogParameters<FolderBrowserDialog> { { dialog => dialog.InitialPath, initialPath } };
        var dialog = await dialogService.ShowAsync<FolderBrowserDialog>("Choose Folder", parameters, FolderBrowserOptions);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string folder } ? folder : null;
    }
}
