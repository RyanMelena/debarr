using Microsoft.AspNetCore.Components;

namespace Debarr.Components;

/// <summary>
/// A table's sort column and direction, for a phone, where the table hides its headers.
/// It shows below 600 px, where the table lays each row out as a card.
/// </summary>
public partial class SortSelect
{
    [Parameter, EditorRequired]
    public IReadOnlyList<SortOption> Options { get; set; } = [];

    [Parameter, EditorRequired]
    public string Sort { get; set; } = "";

    [Parameter]
    public bool Descending { get; set; }

    /// <summary>Receives the column and whether it sorts descending.</summary>
    [Parameter]
    public EventCallback<(string Sort, bool Descending)> SortChanged { get; set; }

    private string DirectionText =>
        Options.FirstOrDefault(option => option.Value == Sort) is { } option
            ? Descending ? option.DescendingText : option.AscendingText
            : "";

    /// <summary>A new column sorts ascending, as a click on its header does.</summary>
    private Task SelectAsync(string sort) => SortChanged.InvokeAsync((sort, false));

    private Task ReverseAsync() => SortChanged.InvokeAsync((Sort, !Descending));
}
