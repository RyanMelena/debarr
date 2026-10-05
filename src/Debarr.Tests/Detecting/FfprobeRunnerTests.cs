using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public class FfprobeRunnerTests
{
    [Fact]
    public void An_anamorphic_matroska_probe_takes_the_duration_from_the_format()
    {
        const string json = """
            {
                "streams": [
                    {
                        "index": 0,
                        "codec_name": "h264",
                        "codec_long_name": "H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
                        "profile": "Constrained Baseline",
                        "codec_type": "video",
                        "width": 720,
                        "height": 480,
                        "coded_width": 720,
                        "coded_height": 480,
                        "sample_aspect_ratio": "32:27",
                        "display_aspect_ratio": "16:9",
                        "pix_fmt": "yuv420p",
                        "level": 22,
                        "color_range": "tv",
                        "field_order": "progressive",
                        "r_frame_rate": "5/1",
                        "avg_frame_rate": "5/1",
                        "time_base": "1/1000",
                        "start_pts": 0,
                        "start_time": "0.000000",
                        "tags": {
                            "ENCODER": "Lavc62.28.102 libx264",
                            "DURATION": "00:01:00.000000000"
                        }
                    }
                ],
                "format": {
                    "filename": "anamorphic-133.mkv",
                    "nb_streams": 1,
                    "format_name": "matroska,webm",
                    "start_time": "0.000000",
                    "duration": "60.000000",
                    "size": "5573720",
                    "bit_rate": "743162",
                    "probe_score": 100
                }
            }
            """;

        var result = FfprobeRunner.Parse(json);

        Assert.True(result.IsSuccess);
        var (containerMetadata, duration) = result.Value;
        Assert.Equal((720, 480, "h264", null), (containerMetadata.Width, containerMetadata.Height, containerMetadata.CodecName, containerMetadata.ColorTransfer));
        Assert.Equal(1.7778, containerMetadata.ContainerAspectRatio.Value, 4);
        Assert.Equal(TimeSpan.FromSeconds(60), duration);
        Assert.False(containerMetadata.IsHdr);
    }

    [Fact]
    public void An_unknown_sample_aspect_ratio_counts_as_square_pixels()
    {
        const string json = """
            {
                "streams": [
                    {
                        "codec_name": "hevc",
                        "width": 3840,
                        "height": 1600,
                        "sample_aspect_ratio": "0:1",
                        "display_aspect_ratio": "N/A",
                        "color_transfer": "smpte2084",
                        "duration": "7200.500000"
                    }
                ],
                "format": { "duration": "7201.000000" }
            }
            """;

        var result = FfprobeRunner.Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal((new ContainerMetadata(new AspectRatio(2.4), 3840, 1600, "hevc", "smpte2084"), TimeSpan.FromSeconds(7200.5)), result.Value);
        Assert.True(result.Value.ContainerMetadata.IsHdr);
    }

    [Fact]
    public void A_probe_without_a_video_stream_fails()
    {
        var result = FfprobeRunner.Parse("""{ "streams": [], "format": {} }""");

        Assert.True(result.IsFailed);
        Assert.Equal("ffprobe found no video stream.", Assert.Single(result.Errors).Message);
    }
}
