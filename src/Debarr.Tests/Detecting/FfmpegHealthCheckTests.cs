using Debarr.Detecting;
using Debarr.Health;
using Debarr.Hosting;
using Microsoft.Extensions.Options;

namespace Debarr.Tests.Detecting;

public sealed class FfmpegHealthCheckTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_that_report_their_versions_find_nothing()
    {
        var check = new FfmpegHealthCheck(new FfmpegVersions(Options.Create(new FfmpegOptions())));

        Assert.Empty(await check.CheckAsync(CancellationToken));
    }

    [Fact]
    public async Task Each_tool_that_cannot_run_is_an_error_that_links_to_general_settings()
    {
        var missing = Path.Combine(AppContext.BaseDirectory, "missing-ffprobe");
        var check = new FfmpegHealthCheck(new FfmpegVersions(Options.Create(new FfmpegOptions { FfprobePath = missing })));

        var message = Assert.Single(await check.CheckAsync(CancellationToken));

        Assert.Equal((HealthSeverity.Error, "settings/general"), (message.Severity, message.Href));
        Assert.StartsWith("Every detection fails until this is fixed: could not run ffprobe:", message.Text);
    }
}
