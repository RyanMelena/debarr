using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>One titled section of a page: a card with the title and its actions, a description, and the content stacked beneath.</summary>
public partial class PageSection
{
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    /// <summary>Actions that apply to the whole section, at the right of the title.</summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    /// <summary>What the section is for, beneath the title.</summary>
    [Parameter]
    public RenderFragment? Description { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
}
