using Debarr.Detecting;
using Debarr.Extensions;

namespace Debarr.Tests.Detecting;

public class PictureMeasurementTests
{
    private static readonly ContainerMetadata Sdr = new(new AspectRatio(1.78), 1920, 1080, "hevc", "bt709");

    private static readonly ContainerMetadata Pq = Sdr with { ColorTransfer = "smpte2084" };

    private static readonly ContainerMetadata Hlg = Sdr with { ColorTransfer = "arib-std-b67" };

    [Fact]
    public void Samples_are_centred_in_equal_shares_of_the_runtime_between_the_skipped_edges()
    {
        var positions = new PictureMeasurement(12, 5, 24, null).GetSamplePositions(TimeSpan.FromSeconds(60));

        Assert.Equal(12, positions.Count);
        Assert.Equal(TimeSpan.FromSeconds(5.25), positions[0]);
        Assert.Equal(TimeSpan.FromSeconds(9.75), positions[1]);
        Assert.Equal(TimeSpan.FromSeconds(54.75), positions[^1]);
    }

    [Fact]
    public void A_pq_or_hlg_file_takes_the_hdr_black_level()
    {
        var pictureMeasurement = new PictureMeasurement(12, 5, 24, 64);

        Assert.Equal((24, 64, 64), (pictureMeasurement.GetBlackLevel(Sdr), pictureMeasurement.GetBlackLevel(Pq), pictureMeasurement.GetBlackLevel(Hlg)));
    }

    [Fact]
    public void An_hdr_file_takes_the_sdr_black_level_when_no_hdr_level_is_set() =>
        Assert.Equal(24, new PictureMeasurement(12, 5, 24, null).GetBlackLevel(Pq));

    [Fact]
    public void Create_refuses_each_value_outside_its_bounds_beneath_its_field()
    {
        var created = PictureMeasurement.Create(0, 46, 255, -1);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["SampleCount"] = "Enter 1 or more.",
                ["SkipStartAndEndPercent"] = "Enter 0 to 45.",
                ["BlackLevelSdr"] = "Enter 0 to 254.",
                ["BlackLevelHdr"] = "Enter 0 to 254.",
            },
            created.GetFieldErrors());
    }

    [Fact]
    public void Create_takes_the_values_at_their_bounds() =>
        Assert.Equal(new PictureMeasurement(1, 45, 254, 0), PictureMeasurement.Create(1, 45, 254, 0).Value);
}
