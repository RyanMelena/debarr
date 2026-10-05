using Debarr.Components;
using MudBlazor;

namespace Debarr.Extensions;

public static class IDialogServiceExtensions
{
    private static readonly DialogOptions ConfirmOptions = new() { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };

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
}
