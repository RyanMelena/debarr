using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Debarr.Components;

/// <summary>One item of a <see cref="MotionList{TItem}"/>, which the list keys so the item keeps its elements while it stays, moves or leaves.</summary>
public sealed class MotionListItem : ComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
}
