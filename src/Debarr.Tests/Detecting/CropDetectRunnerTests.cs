using Debarr.Detecting;


namespace Debarr.Tests.Detecting;

public class CropDetectRunnerTests
{
    [Theory]
    [InlineData(24, "0.094118")]
    [InlineData(0, "0")]
    [InlineData(16, "0.062745")]
    [InlineData(255, "0.996078")]
    [InlineData(-5, "0")]
    public void The_black_limit_goes_over_the_wire_as_a_fraction_below_one(int limit, string expected) =>
        Assert.Equal(expected, CropDetectRunner.FormatLimit(limit));
}
