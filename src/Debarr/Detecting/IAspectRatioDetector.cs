namespace Debarr.Detecting;

/// <summary>Detects one file's aspect ratio with the detection settings.</summary>
public interface IAspectRatioDetector
{
    Task<DetectorOutcome> DetectAsync(
        string path,
        DetectionSettings detectionSettings,
        CancellationToken cancellationToken);
}
