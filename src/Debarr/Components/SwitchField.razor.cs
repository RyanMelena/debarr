using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>A switch on its own row, with its help beneath it where an outlined field puts its own.</summary>
public partial class SwitchField
{
    [Parameter]
    public bool Value { get; set; }

    [Parameter]
    public EventCallback<bool> ValueChanged { get; set; }

    [Parameter, EditorRequired]
    public string Label { get; set; } = "";

    [Parameter]
    public string? HelperText { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
}
