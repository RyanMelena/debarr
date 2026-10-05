using Debarr.Detecting;
using Debarr.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests.Detecting;

public sealed class LibraryWritePauseTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void A_pause_disposed_twice_ends_its_detection_pause_and_then_its_scan_pause_once()
    {
        var ended = new List<string>();
        var pause = new LibraryWritePause(new ScanPause(() => ended.Add("scans")), new DetectionPause(() => ended.Add("detections")));

        pause.Dispose();
        pause.Dispose();

        Assert.Equal(["detections", "scans"], ended);
    }

    [Fact]
    public async Task A_start_cancelled_while_detections_are_paused_ends_the_scan_pause_it_started()
    {
        await using var host = await TestHost.StartAsync();
        var scanner = host.Services.GetRequiredService<LibraryScanner>();
        var orchestrator = host.Services.GetRequiredService<DetectionOrchestrator>();
        using var detectionPause = await orchestrator.PauseDetectionsAsync(CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LibraryWritePause.StartAsync(scanner, orchestrator, cancellation.Token));

        using var scanPause = await scanner.PauseScansAsync(CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
    }
}
