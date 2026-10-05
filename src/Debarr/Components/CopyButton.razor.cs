using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Debarr.Components;

/// <summary>An icon button that copies a value, such as a path, and shows for two seconds that it did.</summary>
public sealed partial class CopyButton(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private static readonly TimeSpan ConfirmationTime = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _disposed = new();
    private IJSObjectReference? _module;
    private bool? _copied;

    [Parameter, EditorRequired]
    public string Value { get; set; } = "";

    /// <summary>The button's label and tooltip, such as Copy Path.</summary>
    [Parameter]
    public string Label { get; set; } = "Copy Path";

    private string Icon => _copied switch
    {
        true => Icons.Material.Outlined.Check,
        false => Icons.Material.Outlined.ErrorOutline,
        null => Icons.Material.Outlined.ContentCopy,
    };

    private string Tooltip => _copied switch
    {
        true => "Copied",
        false => "The browser refused to copy",
        null => Label,
    };

    private string AriaLabel => _copied == true ? "Copied" : Label;

    private async Task CopyAsync()
    {
        try
        {
            _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./Components/CopyButton.razor.js");
            await _module.InvokeVoidAsync("copy", Value);
            _copied = true;
        }
        catch (JSException)
        {
            _copied = false;
        }

        StateHasChanged();
        try
        {
            await Task.Delay(ConfirmationTime, _disposed.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _copied = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync();
        _disposed.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone, and the module with it.
            }
        }
    }
}
