using Bunit;
using Debarr.Components;
using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using MudBlazor.Services;

namespace Debarr.Tests.Components;

public sealed class VideoFileStatusTextTests : BunitContext
{
    public VideoFileStatusTextTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(VideoFileStatus.Detected, "mud-success-text", "Detected")]
    [InlineData(VideoFileStatus.FromFile, "mud-info-text", "From File")]
    [InlineData(VideoFileStatus.Manual, "mud-tertiary-text", "Manual")]
    [InlineData(VideoFileStatus.Failed, "mud-error-text", "Failed")]
    [InlineData(VideoFileStatus.Pending, "mud-text-secondary", "Pending")]
    public void Each_status_has_its_own_colour_and_its_name(VideoFileStatus status, string colorClass, string name)
    {
        var cut = Render<VideoFileStatusText>(parameters => parameters.Add(text => text.Status, status));

        var label = cut.Find(".state-label");
        Assert.Contains(colorClass, label.ClassList);
        Assert.Equal(name, label.TextContent.Trim());
    }

    [Fact]
    public void Each_status_has_its_own_icon()
    {
        var icons = Enum.GetValues<VideoFileStatus>()
            .Select(status => Render<VideoFileStatusText>(parameters => parameters.Add(text => text.Status, status)).Find("svg").InnerHtml)
            .ToList();

        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    [Fact]
    public void A_running_detection_reads_detecting_and_its_end_fades_the_status_back_in()
    {
        var cut = Render<VideoFileStatusText>(parameters => parameters.Add(text => text.Status, VideoFileStatus.Pending));

        cut.Render(parameters => parameters
            .Add(text => text.Running, new RunningDetection(TestFileHash.For("heat"), new LocalPath("/media/heat.mkv"), DetectionOrigin.DetectNow, DateTimeOffset.UtcNow)));
        var detecting = cut.Find(".state-label");
        Assert.Equal("Detecting", detecting.TextContent.Trim());
        Assert.Contains("mud-info-text", detecting.ClassList);
        Assert.Contains("state-label-changed", detecting.ClassList);

        cut.Render(parameters => parameters
            .Add(text => text.Status, VideoFileStatus.Detected)
            .Add(text => text.Running, null));
        var detected = cut.Find(".state-label");
        Assert.Equal("Detected", detected.TextContent.Trim());
        Assert.Contains("state-label-changed", detected.ClassList);
    }
}
