using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public class StringExtensionsTests
{
    [Fact]
    public void Sort_keys_order_ordinally_ignoring_case_and_break_ties_by_the_value()
    {
        string[] values = ["beta", "Alpha", "alpha", "Beta", "ALPHA"];

        Assert.Equal(["ALPHA", "Alpha", "alpha", "Beta", "beta"], values.OrderBy(value => value.ToCaseInsensitiveThenOrdinalSortKey(), StringComparer.Ordinal));
    }
}
