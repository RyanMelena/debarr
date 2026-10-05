using Debarr.Scanning;

namespace Debarr.Detecting;

/// <summary>A detection of the video file ended, whether it succeeded, failed or was cancelled. The video file holds the outcome.</summary>
/// <param name="Path">The file path the detection read first.</param>
public sealed record DetectionFinishedEvent(FileHash VideoFile, LocalPath Path) : VideoFileEvent(VideoFile);
