using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public class CropSampleExtensionsTests
{
    [Fact]
    public void Eleven_letterboxed_samples_and_one_full_frame_give_the_letterbox_ratio()
    {
        List<CropSample> samples =
        [
            .. Enumerable.Range(0, 11).Select(index => new CropSample(TimeSpan.FromSeconds(index), new CropBox(1920, 804))),
            new CropSample(TimeSpan.FromSeconds(11), new CropBox(1920, 1080)),
        ];

        var aggregate = samples.AggregateAspectRatio();

        Assert.NotNull(aggregate);
        Assert.Equal(2.388, aggregate.Value.AspectRatio, 3);
        Assert.Equal(0.917, aggregate.Value.Confidence);
    }

    [Fact]
    public void Boxes_within_four_pixels_share_a_bucket_and_samples_without_a_box_lower_confidence()
    {
        List<CropSample> samples =
        [
            new(TimeSpan.FromSeconds(1), new CropBox(1920, 800)),
            new(TimeSpan.FromSeconds(2), new CropBox(1920, 804)),
            new(TimeSpan.FromSeconds(3), new CropBox(1920, 1038)),
            new(TimeSpan.FromSeconds(4), null),
        ];

        var aggregate = samples.AggregateAspectRatio();

        Assert.NotNull(aggregate);
        Assert.Equal((1920.0 / 800 + 1920.0 / 804) / 2, aggregate.Value.AspectRatio, 9);
        Assert.Equal(0.5, aggregate.Value.Confidence);
    }

    // Each bucket has two boxes; the heights say which bucket forms first.
    [Theory]
    [InlineData(804, 1038, 1920.0 / 804)]
    [InlineData(1038, 804, 1920.0 / 1038)]
    public void A_tie_between_buckets_goes_to_the_bucket_that_formed_first(int firstHeight, int secondHeight, double expected)
    {
        List<CropSample> samples =
        [
            new(TimeSpan.FromSeconds(1), new CropBox(1920, firstHeight)),
            new(TimeSpan.FromSeconds(2), new CropBox(1920, secondHeight)),
            new(TimeSpan.FromSeconds(3), new CropBox(1920, secondHeight)),
            new(TimeSpan.FromSeconds(4), new CropBox(1920, firstHeight)),
        ];

        var aggregate = samples.AggregateAspectRatio();

        Assert.NotNull(aggregate);
        Assert.Equal(expected, aggregate.Value.AspectRatio, 9);
        Assert.Equal(0.5, aggregate.Value.Confidence);
    }

    [Fact]
    public void Samples_without_a_box_give_no_aggregate()
    {
        List<CropSample> samples = [new(TimeSpan.FromSeconds(1), null), new(TimeSpan.FromSeconds(2), null)];

        Assert.Null(samples.AggregateAspectRatio());
    }

    [Fact]
    public void The_agreeing_samples_are_the_largest_bucket_and_leave_out_other_boxes_and_samples_without_a_box()
    {
        List<CropSample> samples =
        [
            new(TimeSpan.FromSeconds(1), new CropBox(1920, 800)),
            new(TimeSpan.FromSeconds(2), new CropBox(1920, 1038)),
            new(TimeSpan.FromSeconds(3), null),
            new(TimeSpan.FromSeconds(4), new CropBox(1920, 804)),
        ];

        Assert.Equal([samples[0], samples[3]], samples.AgreeingSamples());
    }

    [Fact]
    public void Samples_without_a_box_have_no_agreeing_samples()
    {
        List<CropSample> samples = [new(TimeSpan.FromSeconds(1), null)];

        Assert.Empty(samples.AgreeingSamples());
    }
}
