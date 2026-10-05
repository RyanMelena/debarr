using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public class DoubleExtensionsTests
{
    [Theory]
    [InlineData(2.4, "2.40")]
    [InlineData(1.78, "1.78")]
    [InlineData(2.355, "2.355")]
    [InlineData(1.77777, "1.7778")]
    public void A_ratio_shows_two_decimals_or_as_many_more_as_it_holds_up_to_four(double aspectRatio, string expected) =>
        Assert.Equal(expected, aspectRatio.ToAspectRatioText());

    [Theory]
    [InlineData(2.4, "2.400")]
    [InlineData(2.38712, "2.387")]
    public void A_raw_ratio_shows_three_decimals(double aspectRatio, string expected) =>
        Assert.Equal(expected, aspectRatio.ToRawAspectRatioText());

    [Theory]
    [InlineData(0.9, "90%")]
    [InlineData(0.926, "93%")]
    [InlineData(1.0, "100%")]
    public void A_fraction_shows_as_a_whole_percentage(double fraction, string expected) =>
        Assert.Equal(expected, fraction.ToPercentText());
}
