using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>Toggles <see cref="Components.ShowAdvanced"/> for every page and modal.</summary>
public partial class ShowAdvancedButton
{
    [CascadingParameter]
    private ShowAdvanced ShowAdvanced { get; set; } = default!;
}
