using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

public partial class IntegrationModal
{
    [Parameter, EditorRequired]
    public IntegrationModalBase Modal { get; set; } = default!;

    /// <summary>Leads the id of each alert and button, such as kodi for kodi-save.</summary>
    [Parameter, EditorRequired]
    public string IdPrefix { get; set; } = "";

    /// <summary>True for a saved integration, which Remove removes.</summary>
    [Parameter]
    public bool Removable { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
