using Bunit;
using Debarr.Components;
using MudBlazor.Services;

namespace Debarr.Tests.Components;

public sealed class TableSkeletonTests : BunitContext
{
    public TableSkeletonTests() => Services.AddMudServices();

    [Fact]
    public void A_skeleton_holds_a_row_of_placeholders_for_each_expected_row()
    {
        var cut = Render<TableSkeleton>(parameters => parameters
            .Add(skeleton => skeleton.Rows, 3)
            .Add(skeleton => skeleton.Columns, 6)
            .Add(skeleton => skeleton.TwoLines, true));

        var rows = cut.FindAll("tr.table-skeleton-row");
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal("true", row.GetAttribute("aria-hidden")));
        Assert.All(rows, row => Assert.Equal(6, row.QuerySelectorAll("td").Length));
        Assert.Equal(2, rows[0].QuerySelectorAll("td")[0].QuerySelectorAll(".mud-skeleton").Length);
    }
}
