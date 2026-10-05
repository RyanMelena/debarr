using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public class AspectRatioTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-2.39)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_aspect_ratio_is_a_finite_number_greater_than_0(double value)
    {
        Assert.False(AspectRatio.IsValid(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AspectRatio(value));
    }

    [Fact]
    public void Two_aspect_ratios_of_the_same_value_are_equal() =>
        Assert.Equal(new AspectRatio(2.39), new AspectRatio(2.39));
}
