using Microsoft.AspNetCore.Components;

namespace Debarr.Components.Pages;

public partial class ErrorPage
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }

    private bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    protected override void OnInitialized() =>
        RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
}
