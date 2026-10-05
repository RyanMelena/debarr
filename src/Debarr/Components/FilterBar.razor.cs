using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>The filters above a list: a chip for each filter with its count at the left, then any select and the search box at the right.</summary>
public partial class FilterBar
{
    /// <summary>The start of each element's id: the chips are <c>{Id}-filters</c>, a chip <c>{Id}-filter-{value}</c> and the search box <c>{Id}-search</c>.</summary>
    [Parameter, EditorRequired]
    public string Id { get; set; } = "";

    [Parameter, EditorRequired]
    public IEnumerable<FilterChip> Chips { get; set; } = [];

    /// <summary>The selected chip's value.</summary>
    [Parameter]
    public string? SelectedFilter { get; set; }

    [Parameter]
    public EventCallback<string?> SelectedFilterChanged { get; set; }

    [Parameter]
    public string? Search { get; set; }

    [Parameter]
    public EventCallback<string?> SearchChanged { get; set; }

    [Parameter, EditorRequired]
    public string SearchPlaceholder { get; set; } = "";

    /// <summary>The selects between the chips and the search box.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
