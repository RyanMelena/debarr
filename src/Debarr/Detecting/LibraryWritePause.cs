using Debarr.Scanning;

namespace Debarr.Detecting;

/// <summary>
/// A scan pause and then a detection pause, for a command that writes to every video file it reaches in one transaction, or archives them,
/// which a scan's or a detection's commit to one of them would fail.
/// Dispose ends the detection pause and then the scan pause, each once however often it is called.
/// </summary>
public sealed class LibraryWritePause(ScanPause scanPause, DetectionPause detectionPause) : IDisposable
{
    /// <summary>Pauses scans, which stops the running library or folder scan, then pauses detections, and ends the scan pause when pausing detections fails.</summary>
    public static async Task<LibraryWritePause> StartAsync(LibraryScanner scanner, DetectionOrchestrator orchestrator, CancellationToken cancellationToken)
    {
        var scanPause = await scanner.PauseScansAsync(cancellationToken);
        try
        {
            return new LibraryWritePause(scanPause, await orchestrator.PauseDetectionsAsync(cancellationToken));
        }
        catch
        {
            scanPause.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        detectionPause.Dispose();
        scanPause.Dispose();
    }
}
