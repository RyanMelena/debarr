using System.Globalization;
using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public class TimeSpanExtensionsTests
{
    [Theory]
    [InlineData(850, "850 ms")]
    [InlineData(3_200, "3.2 s")]
    [InlineData(245_000, "4 min 5 s")]
    [InlineData(7_380_000, "2 h 3 min")]
    public void A_duration_shows_in_its_two_largest_units(int milliseconds, string expected) =>
        Assert.Equal(expected, TimeSpan.FromMilliseconds(milliseconds).ToDisplayText());

    [Fact]
    public void A_duration_shows_a_decimal_point_on_a_host_whose_culture_writes_a_comma()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.Equal("3.2 s", TimeSpan.FromMilliseconds(3_200).ToDisplayText());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
