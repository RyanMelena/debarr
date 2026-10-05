using Debarr.Detecting;
using Debarr.Extensions;
using FluentResults;

namespace Debarr.Tests.Detecting;

public class StandardRatiosTests
{
    private static readonly StandardRatios Default = StandardRatios.Default;

    [Fact]
    public void The_defaults_are_the_eight_standard_ratios_with_1_33_and_1_78_checking_the_picture()
    {
        Assert.Equal([1.33, 1.66, 1.78, 1.85, 2.00, 2.20, 2.35, 2.39], Default.Ratios.Select(ratio => ratio.AspectRatio));
        Assert.Equal([1.33, 1.78], Default.Ratios.Where(ratio => ratio.ChecksPicture).Select(ratio => ratio.AspectRatio));
        Assert.Equal(0.04, Default.MatchTolerance);
    }

    // 2.43, 1.89 and 2.16 sit exactly one tolerance away from their snap; 2.37 is equally near 2.35 and 2.39.
    [Theory]
    [InlineData(2.388, 2.39)]
    [InlineData(1.80, 1.78)]
    [InlineData(1.82, 1.85)]
    [InlineData(2.43, 2.39)]
    [InlineData(1.89, 1.85)]
    [InlineData(2.16, 2.20)]
    [InlineData(2.37, 2.35)]
    public void A_ratio_snaps_to_the_nearest_standard_ratio_within_the_tolerance(double raw, double expected) =>
        Assert.Equal(expected, Default.Snap(new AspectRatio(raw)).Value);

    // 2.431 is just outside 2.39's tolerance; 2.125 checks midpoint rounding away from zero.
    [Theory]
    [InlineData(2.10, 2.10)]
    [InlineData(2.15, 2.15)]
    [InlineData(2.431, 2.43)]
    [InlineData(2.125, 2.13)]
    [InlineData(2.5678, 2.57)]
    public void A_ratio_outside_the_tolerance_of_every_standard_ratio_keeps_its_raw_value_to_two_decimals(double raw, double expected)
    {
        var snapped = Default.Snap(new AspectRatio(raw));

        Assert.Equal(expected, snapped.Value);
        Assert.Null(snapped.Match);
        Assert.False(snapped.ChecksPicture);
    }

    [Fact]
    public void The_snapped_ratio_names_the_matched_standard_ratio_and_whether_it_checks_the_picture()
    {
        var checksPicture = Default.Snap(new AspectRatio(1.79));
        var skipsPicture = Default.Snap(new AspectRatio(2.36));

        Assert.Equal(new StandardRatio(1.78, true), checksPicture.Match);
        Assert.True(checksPicture.ChecksPicture);
        Assert.Equal(new StandardRatio(2.35, false), skipsPicture.Match);
        Assert.False(skipsPicture.ChecksPicture);
    }

    [Fact]
    public void A_tie_goes_to_the_smaller_ratio_whatever_the_list_order()
    {
        var reversed = StandardRatios.Create(Default.Ratios.Reverse(), Default.MatchTolerance).Value;

        Assert.Equal(2.35, reversed.Snap(new AspectRatio(2.37)).Value);
    }

    [Fact]
    public void A_wider_tolerance_widens_the_match()
    {
        var wide = StandardRatios.Create(Default.Ratios, 0.10).Value;

        Assert.Equal(2.00, wide.Snap(new AspectRatio(2.10)).Value);
    }

    [Fact]
    public void Create_refuses_an_empty_list() =>
        AssertFieldError(StandardRatios.Create([], 0.04), "StandardRatios", "Add at least one standard ratio.");

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public void Create_refuses_a_ratio_of_0_or_less(double aspectRatio) =>
        AssertFieldError(StandardRatios.Create([new(aspectRatio, false)], 0.04), "StandardRatios", "Every ratio must be greater than 0.");

    [Fact]
    public void Create_refuses_a_ratio_listed_twice_to_two_decimals() =>
        AssertFieldError(StandardRatios.Create([new(1.85, false), new(1.849, true)], 0.04), "StandardRatios", "1.85 is listed twice.");

    [Theory]
    [InlineData(0.009)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void Create_refuses_a_match_tolerance_under_0_01(double matchTolerance) =>
        AssertFieldError(StandardRatios.Create(Default.Ratios, matchTolerance), "MatchTolerance", "Enter 0.01 or more.");

    [Fact]
    public void Create_reports_the_list_and_the_tolerance_together()
    {
        var created = StandardRatios.Create([], 0);

        Assert.Equal(["StandardRatios", "MatchTolerance"], created.GetFieldErrors().Keys);
    }

    [Fact]
    public void Create_orders_the_ratios_ascending() =>
        Assert.Equal([1.66, 2.39], StandardRatios.Create([new(2.39, false), new(1.66, false)], 0.04).Value.Ratios.Select(ratio => ratio.AspectRatio));

    private static void AssertFieldError(Result<StandardRatios> created, string field, string message) =>
        Assert.Equal(new Dictionary<string, string> { [field] = message }, created.GetFieldErrors());
}
