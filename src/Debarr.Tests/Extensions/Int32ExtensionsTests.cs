using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public class Int32ExtensionsTests
{
    [Theory]
    [InlineData(0, "0 video files")]
    [InlineData(1, "1 video file")]
    [InlineData(1204, "1,204 video files")]
    public void A_count_takes_thousands_separators_and_the_noun_that_agrees_with_it(int count, string expected) =>
        Assert.Equal(expected, count.ToCountText("video file", "video files"));

    [Fact]
    public void A_bare_count_takes_thousands_separators() =>
        Assert.Equal("50,000", 50000.ToCountText());
}
