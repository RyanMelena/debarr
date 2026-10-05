using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Debarr.Components;

/// <summary>
/// A paged table's view and the URL that holds it: the page's filters, the sort, the direction, the page and the page size.
/// A <see cref="PagedTable{TRow}"/> shows it, and the page that owns it reads its rows from the database.
/// </summary>
public sealed class PagedTableView<TRow>(NavigationManager navigation, IReadOnlyList<SortOption> sortOptions, TableView defaultView)
{
    private readonly TableView _defaultView = defaultView;
    private IReadOnlyDictionary<string, string?> _filters = new Dictionary<string, string?>();

    // True while the view applies a page size from the URL, whose page wins over the first page the table reports on a new page size.
    private bool _applyingUrlView;

    private (int Page, int PageSize, TableData<TRow> Data)? _lastRead;

    private HashSet<TRow> _rowsBroughtByLiveReload = [];
    private bool _reloadingLive;

    public IReadOnlyList<SortOption> SortOptions => sortOptions;

    public TableView Current { get; private set; } = defaultView;

    public MudTable<TRow>? Table { get; set; }

    /// <summary>
    /// Takes the filters and the view from the URL, and says whether the filters or the sort, page or page size changed.
    /// A URL whose sort is unknown, such as a link from before a sort was renamed, is replaced with the view shown.
    /// </summary>
    /// <param name="filters">The page's query parameters, by name, with null for each value that is its default.</param>
    public (bool FiltersChanged, bool ViewChanged) TakeFromUrl(IReadOnlyDictionary<string, string?> filters, string? sort, string? direction, int? page, int? pageSize)
    {
        var view = TableView.FromQuery(sort, direction, page, pageSize, sortOptions, _defaultView);
        var changes = (!SameFilters(filters), view != Current);
        _filters = filters;
        Current = view;
        if (sort is not null && TableView.FindSort(sort, sortOptions) is null)
        {
            UpdateUrl();
        }

        return changes;
    }

    /// <summary>Shows the filters and the view a link or the back button puts in the URL while the table is on screen, reading the counts first when the filters changed.</summary>
    public async Task ShowFromUrlAsync(IReadOnlyDictionary<string, string?> filters, string? sort, string? direction, int? page, int? pageSize, Func<Task> readCounts)
    {
        var shownPageSize = Current.PageSize;
        var (filtersChanged, viewChanged) = TakeFromUrl(filters, sort, direction, page, pageSize);
        if (Table is null || !(filtersChanged || viewChanged))
        {
            return;
        }

        if (filtersChanged)
        {
            await readCounts();
        }

        if (Current.PageSize != shownPageSize)
        {
            // SetRowsPerPage reads the page on screen, which is the URL's page when the page renders during it.
            _applyingUrlView = true;
            try
            {
                Table.SetRowsPerPage(Current.PageSize);
            }
            finally
            {
                _applyingUrlView = false;
            }

            if (Table.CurrentPage == Current.Page)
            {
                return;
            }
        }

        await ShowPageAsync();
    }

    /// <summary>Reads the page on screen again after a commit, fading in the rows it brings.</summary>
    public async Task ReloadAsync()
    {
        if (Table is null)
        {
            return;
        }

        _reloadingLive = true;
        try
        {
            await Table.ReloadServerData();
        }
        finally
        {
            _reloadingLive = false;
        }
    }

    /// <summary>Reads one page of rows through <paramref name="readPage"/>, and keeps the rows a live reload brought.</summary>
    public async Task<TableData<TRow>> ReadAsync(TableState state, Func<TableState, CancellationToken, Task<TableData<TRow>>> readPage, CancellationToken cancellationToken)
    {
        // While the view applies a page size from the URL, the table can ask for the first page before the URL's page, or for one page twice.
        // Only the URL's page is read; the other requests get the last read, which the URL's page replaces.
        if (_applyingUrlView && _lastRead is { } lastRead
            && (state.Page != Current.Page || (lastRead.Page, lastRead.PageSize) == (state.Page, state.PageSize)))
        {
            return lastRead.Data;
        }

        var data = await readPage(state, cancellationToken);
        _rowsBroughtByLiveReload = _reloadingLive && _lastRead is { } previousRead ? [.. (data.Items ?? []).Except(previousRead.Data.Items ?? [])] : [];
        _lastRead = (state.Page, state.PageSize, data);
        return data;
    }

    public string? RowClass(TRow row, int index) => _rowsBroughtByLiveReload.Contains(row) ? "paged-table-row-enter" : null;

    public SortDirection DirectionOf(string sort) =>
        sort != Current.Sort ? SortDirection.None : Current.Descending ? SortDirection.Descending : SortDirection.Ascending;

    /// <summary>A header click: the table reads its rows again once this returns.</summary>
    public void SortChanged(string sort, SortDirection direction)
    {
        Current = Current with { Sort = sort, Descending = direction == SortDirection.Descending };
        UpdateUrl();
    }

    public async Task SortAsync((string Sort, bool Descending) sort)
    {
        Current = Current with { Sort = sort.Sort, Descending = sort.Descending };
        UpdateUrl();
        await ShowPageAsync();
    }

    public void PageChanged(int page)
    {
        if (_applyingUrlView || page == Current.Page)
        {
            return;
        }

        Current = Current with { Page = page };
        UpdateUrl();
    }

    /// <summary>A new page size shows the first page, as the table does.</summary>
    public void PageSizeChanged(int pageSize)
    {
        if (_applyingUrlView || pageSize == Current.PageSize)
        {
            return;
        }

        Current = Current with { Page = 0, PageSize = pageSize };
        UpdateUrl();
    }

    /// <summary>Puts a filter's new value in the URL, which shows its first page.</summary>
    public void Filter(string name, string? value)
    {
        var query = Query();
        query[name] = value;
        query["page"] = null;
        navigation.NavigateTo(navigation.GetUriWithQueryParameters(query), replace: true);
    }

    private async Task ShowPageAsync()
    {
        if (Table is null)
        {
            return;
        }

        if (Table.CurrentPage == Current.Page)
        {
            await Table.ReloadServerData();
        }
        else
        {
            Table.NavigateTo(Current.Page);
        }
    }

    private bool SameFilters(IReadOnlyDictionary<string, string?> filters) =>
        filters.Count == _filters.Count && filters.All(filter => _filters.TryGetValue(filter.Key, out var value) && value == filter.Value);

    private void UpdateUrl() => navigation.NavigateTo(navigation.GetUriWithQueryParameters(Query()), replace: true);

    private Dictionary<string, object?> Query()
    {
        var query = _filters.ToDictionary(filter => filter.Key, filter => (object?)filter.Value);
        Current.AddTo(query, _defaultView);
        return query;
    }
}
