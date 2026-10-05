using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Scanning;
using Fisher;
using FluentResults;
using Wolverine.Runtime;

namespace Debarr.Detecting;

/// <summary>
/// Runs one detection of a video file, and records the detection with its detection result or failure.
/// It detects on the most recently hashed file path that still holds the hashed file, and runs a file scan on each path that no longer does.
/// </summary>
public sealed partial class DetectionRunner(
    IDocumentStore store,
    IWolverineRuntime runtime,
    IAspectRatioDetector detector,
    LibraryScanner scanner,
    TimeProvider timeProvider,
    ILogger<DetectionRunner> logger)
{
    /// <summary>
    /// Detects the running detection's video file and records the detection.
    /// It tries the file paths, most recently hashed first, and detects on the first one that is unchanged and opens.
    /// A path that changed or fails to open gets a file scan instead.
    /// When the file changes during detection, its path gets a file scan and the result is dropped.
    /// A cancelled detection records nothing.
    /// </summary>
    public async Task RunAsync(RunningDetection runningDetection, CancellationToken cancellationToken)
    {
        var fileHash = runningDetection.VideoFile;

        VideoFile? videoFile;
        DetectionSettings detectionSettings;
        await using (var session = store.QuerySession())
        {
            videoFile = await VideoFile.ReadAsync(session, fileHash, cancellationToken);
            detectionSettings = await DetectionSettings.ReadAsync(session, cancellationToken);
        }

        if (videoFile is null)
        {
            return;
        }

        foreach (var (localPath, stat, _, _) in videoFile.FilePaths)
        {
            var file = new FileInfo(localPath.Value);
            if (!stat.Matches(file) || !file.CanOpen())
            {
                await scanner.ScanFileAsync(localPath, cancellationToken);
                continue;
            }

            var startedAt = timeProvider.GetUtcNow();
            var startedTimestamp = timeProvider.GetTimestamp();
            var outcome = await DetectAsync(localPath.Value, detectionSettings, cancellationToken);
            var duration = timeProvider.GetElapsedTime(startedTimestamp);

            file.Refresh();
            if (!stat.Matches(file))
            {
                await scanner.ScanFileAsync(localPath, cancellationToken);
                return;
            }

            var recorded = await runtime.SendCommandAsync(
                new RecordDetection(Guid.CreateVersion7(), fileHash, runningDetection.Origin, localPath, startedAt, duration, AspectRatioDetector.Version, outcome),
                cancellationToken);
            if (recorded.IsFailed)
            {
                LogRecordFailed(localPath.Value, recorded.GetFormError());
            }

            return;
        }
    }

    /// <summary>Runs the detector, with an exception it throws as the detection's failure.</summary>
    private async Task<DetectorOutcome> DetectAsync(
        string path,
        DetectionSettings detectionSettings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await detector.DetectAsync(path, detectionSettings, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDetectorFailed(exception, path);
            return new DetectorOutcome(null, null, Result.Fail(exception.Message));
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not record the detection of {Path}. {Error}")]
    private partial void LogRecordFailed(string path, string? error);

    [LoggerMessage(LogLevel.Error, "The detector failed on {Path}.")]
    private partial void LogDetectorFailed(Exception exception, string path);
}
