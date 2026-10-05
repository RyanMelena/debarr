using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>A table that reads one page of rows at a time from the database, in the view its <see cref="PagedTableView{TRow}"/> keeps in the URL.</summary>
public partial class PagedTable<TRow>
{
    [Parameter, EditorRequired]
    public PagedTableView<TRow> View { get; set; } = default!;

    [Parameter, EditorRequired]
    public string Id { get; set; } = "";

    /// <summary>Reads one page of rows, filtered, searched and sorted in the database.</summary>
    [Parameter, EditorRequired]
    public Func<TableState, CancellationToken, Task<TableData<TRow>>> ReadPage { get; set; } = default!;

    /// <summary>The rows the filters and the search match, which sets how many placeholder rows the first read shows.</summary>
    [Parameter, EditorRequired]
    public int RowCount { get; set; }

    [Parameter, EditorRequired]
    public int Columns { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment HeaderContent { get; set; } = default!;

    [Parameter, EditorRequired]
    public RenderFragment<TRow> RowTemplate { get; set; } = default!;

    [Parameter, EditorRequired]
    public RenderFragment NoRecordsContent { get; set; } = default!;

    private int SkeletonRows => Math.Clamp(RowCount - (View.Current.Page * View.Current.PageSize), 0, View.Current.PageSize);

    private Task<TableData<TRow>> ReadAsync(TableState state, CancellationToken cancellationToken) => View.ReadAsync(state, ReadPage, cancellationToken);
}
