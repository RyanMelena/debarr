using AngleSharp.Dom;

namespace Debarr.Tests.Components;

/// <summary>Checks a stacked table's cells name their column, which a phone's card shows beside each value.</summary>
public static class StackedTableAssert
{
    /// <summary>
    /// Every cell of a row that has one cell per column carries its column's header as its data-label,
    /// except the first cell, which may leave it out to head the card, and a cell whose column has no header.
    /// </summary>
    public static void EachCellIsLabelledByItsColumn(IElement table)
    {
        Assert.Contains("stacked-table", table.ClassName ?? "");
        var headers = table.QuerySelectorAll("thead th").Select(header => header.TextContent.Trim()).ToList();
        var rows = table.QuerySelectorAll("tbody tr").Where(row => row.Children.Length == headers.Count).ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            foreach (var (cell, column) in row.Children.Select((cell, column) => (cell, column)))
            {
                var label = cell.GetAttribute("data-label");
                if (headers[column] == "" || (column == 0 && label is null))
                {
                    Assert.Null(label);
                    continue;
                }

                Assert.Equal(headers[column], label);
            }
        }
    }
}
