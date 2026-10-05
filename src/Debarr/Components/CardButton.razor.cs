using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Debarr.Components;

/// <summary>An outlined card that acts as one button, such as a player's card that opens its modal, so Tab reaches it and Enter or Space clicks it.</summary>
public partial class CardButton
{
    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
}
