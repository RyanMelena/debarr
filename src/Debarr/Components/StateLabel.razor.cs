using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>
/// A state's text in the state's colour, after an icon that says the same without the colour.
/// A change of state, a new icon or colour, fades the new state in.
/// </summary>
public partial class StateLabel
{
    private (string Icon, Color Color)? _state;
    private bool _changed;

    [Parameter, EditorRequired]
    public string Icon { get; set; } = "";

    /// <summary>Default shows the state in secondary text, for a state that needs no attention.</summary>
    [Parameter]
    public Color Color { get; set; } = Color.Default;

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnParametersSet()
    {
        _changed |= _state is { } state && state != (Icon, Color);
        _state = (Icon, Color);
    }

    private string ColorClass => Color switch
    {
        Color.Default or Color.Inherit => "mud-text-secondary",
        _ => $"mud-{Color.ToString().ToLowerInvariant()}-text",
    };
}
