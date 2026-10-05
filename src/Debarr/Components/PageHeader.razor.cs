using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>
/// The top of every page: its title, and its actions at the right, held under the app bar while the page scrolls.
/// An action that fails shows its error beneath.
/// </summary>
public partial class PageHeader
{
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    /// <summary>A line beneath the title, such as the folder that holds a video file.</summary>
    [Parameter]
    public string? Subtitle { get; set; }

    /// <summary>The page's actions, such as Save or Detect Now.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Why the last action failed; null once it succeeds.</summary>
    [Parameter]
    public string? Error { get; set; }
}
