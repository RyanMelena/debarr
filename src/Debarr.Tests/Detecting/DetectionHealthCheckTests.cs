using Debarr.Detecting;
using Debarr.Health;
using Fisher;

namespace Debarr.Tests.Detecting;

public sealed class DetectionHealthCheckTests : AppTestContext
{
    [Fact]
    public async Task No_failed_file_finds_nothing() =>
        Assert.Empty(await CheckAsync<DetectionHealthCheck>());

    [Fact]
    public async Task Failed_video_files_are_a_warning_that_links_to_media_filtered_to_them_and_counts_each_once()
    {
        await SeedFailedVideoFileAsync("/media/a.mkv", "/backup/a.mkv");
        await SeedFailedVideoFileAsync("/media/b.mkv");

        var message = Assert.Single(await CheckAsync<DetectionHealthCheck>());

        Assert.Equal(
            (HealthSeverity.Warning, "2 files failed detection. A playback of one sends the player's ratio.", "?status=failed"),
            (message.Severity, message.Text, message.Href));
    }

    [Fact]
    public async Task The_check_runs_again_when_a_commit_changes_a_media_row()
    {
        await WaitForMessagesAsync(messages => !messages.Any(message => message.Href == "?status=failed"));

        await SeedFailedVideoFileAsync("/media/a.mkv");

        await WaitForMessagesAsync(messages => messages.Any(message => message.Text.StartsWith("1 file failed detection.", StringComparison.Ordinal)));
    }

    private async Task SeedFailedVideoFileAsync(params string[] paths)
    {
        var store = GetAppService<IDocumentStore>();
        await TestVideoFile.AddAsync(store, paths, CancellationToken, followedBy: videoFile => [new DetectionFailed(videoFile, TestVideoFile.Failed("ffprobe exited 1.", paths[0]))]);
    }
}
