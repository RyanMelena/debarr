using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public sealed class DetectionFailureExplanationTests
{
    private const string Path = @"C:\media\Broken\not a video (100%).mkv";

    [Fact]
    public void An_unreadable_file_says_ffprobe_could_not_read_it_and_keeps_ffprobes_words_without_the_address_or_path()
    {
        var explanation = DetectionFailureExplanation.FromError(
            $"ffprobe exited 1: [matroska,webm @ 000001711537dec0] EBML header parsing failed\r\n{Path}: Invalid data found when processing input",
            Path);

        Assert.Equal("ffprobe could not read the file.", explanation.Summary);
        Assert.StartsWith("The file may be damaged, incomplete or not a video.", explanation.Advice);
        Assert.Null(explanation.Href);
        Assert.Equal("EBML header parsing failed. Invalid data found when processing input.", explanation.Detail);
    }

    [Fact]
    public void A_failed_sample_says_where_in_the_file_ffmpeg_stopped()
    {
        var explanation = DetectionFailureExplanation.FromError(
            "ffmpeg exited 1 sampling at 3725.5s: [h264 @ 0x55d0c8a4e2c0] Invalid NAL unit size | Error while decoding stream #0:0",
            Path);

        Assert.Equal("ffmpeg could not read the picture 1:02:05 into the file.", explanation.Summary);
        Assert.Equal("Invalid NAL unit size. Error while decoding stream #0:0.", explanation.Detail);
    }

    [Fact]
    public void A_timeout_links_to_the_detection_settings()
    {
        var explanation = DetectionFailureExplanation.FromError("timed out after 120s (3/10 samples)");

        Assert.Equal("Detection took longer than its 120 s timeout, after 3 of 10 samples.", explanation.Summary);
        Assert.Equal(("settings/detection", "Fix in Settings > Detection"), (explanation.Href, explanation.LinkText));
        Assert.Null(explanation.Detail);
    }

    [Theory]
    [InlineData("could not run ffmpeg or ffprobe: The system cannot find the file specified.", "ffmpeg or ffprobe")]
    [InlineData("could not run ffprobe: The system cannot find the file specified.", "ffprobe")]
    [InlineData("ffmpeg -version exited 1: bad option", "ffmpeg")]
    public void A_tool_that_cannot_run_links_to_the_general_settings(string error, string tool)
    {
        var explanation = DetectionFailureExplanation.FromError(error);

        Assert.Equal($"Debarr could not run {tool}.", explanation.Summary);
        Assert.Equal(("settings/general", "Fix in Settings > General"), (explanation.Href, explanation.LinkText));
    }

    [Fact]
    public void No_crop_suggests_an_override()
    {
        var explanation = DetectionFailureExplanation.FromError("no crop detected in 10 samples");

        Assert.Equal("None of the 10 samples found the edges of the picture.", explanation.Summary);
        Assert.Contains("override", explanation.Advice);
    }

    [Fact]
    public void An_ffprobe_answer_with_no_video_says_ffprobe_could_not_read_the_file()
    {
        var error = FfprobeRunner.Parse("""{"streams":[],"format":{}}""").Errors.Single().Message;

        var explanation = DetectionFailureExplanation.FromError(error);

        Assert.Equal("ffprobe could not read the file.", explanation.Summary);
        Assert.Equal(error, explanation.Detail);
    }

    [Fact]
    public void Any_other_error_points_to_the_log()
    {
        var explanation = DetectionFailureExplanation.FromError("Object reference not set to an instance of an object");

        Assert.Equal("Detection failed.", explanation.Summary);
        Assert.Equal(("system/logs", "Object reference not set to an instance of an object."), (explanation.Href, explanation.Detail));
    }
}
