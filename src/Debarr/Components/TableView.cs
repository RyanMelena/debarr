namespace Debarr.Components;

/// <summary>
/// The part of a paged table's view that lives in the URL, so a link or the back button returns to it: the sort column, the direction, the page and the page size.
/// The URL leaves out each value that is its default.
/// </summary>
/// <param name="Page">The page, counted from 0; the URL counts from 1.</param>
public sealed record TableView(string Sort, bool Descending, int Page, int PageSize)
{
    public const int DefaultPageSize = 50;

    public static readonly int[] PageSizes = [25, 50, 100];

    /// <summary>
    /// The view the query values name, with the default for each value that is absent or unknown.
    /// A column other than the default sorts ascending unless the URL says otherwise, and an unknown column takes the default sort and its direction.
    /// </summary>
    public static TableView FromQuery(string? sort, string? direction, int? page, int? pageSize, IEnumerable<SortOption> sortOptions, TableView defaultView)
    {
        var known = FindSort(sort, sortOptions);
        var column = known ?? defaultView.Sort;
        var descending = (sort is null || known is not null ? direction?.ToLowerInvariant() : null) switch
        {
            "desc" => true,
            "asc" => false,
            _ => column == defaultView.Sort && defaultView.Descending,
        };
        return new TableView(
            column,
            descending,
            page is > 1 ? page.Value - 1 : 0,
            pageSize is { } size && PageSizes.Contains(size) ? size : DefaultPageSize);
    }

    /// <summary>The sort option's value that <paramref name="sort"/> names, ignoring case; null when it names none.</summary>
    public static string? FindSort(string? sort, IEnumerable<SortOption> sortOptions) =>
        sortOptions.FirstOrDefault(option => string.Equals(option.Value, sort, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Adds the view's query parameters to <paramref name="query"/>, with null for each value that is its default.</summary>
    public void AddTo(Dictionary<string, object?> query, TableView defaultView)
    {
        var defaultDescending = Sort == defaultView.Sort && defaultView.Descending;
        query["sort"] = Sort == defaultView.Sort ? null : Sort;
        query["dir"] = Descending == defaultDescending ? null : Descending ? "desc" : "asc";
        query["page"] = Page > 0 ? Page + 1 : null;
        query["size"] = PageSize == DefaultPageSize ? null : PageSize;
    }
}
