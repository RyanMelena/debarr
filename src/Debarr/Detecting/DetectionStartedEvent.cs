using Debarr.Scanning;

namespace Debarr.Detecting;

/// <summary>A detection of the video file started.</summary>
/// <param name="Path">The file path the detection reads first.</param>
public sealed record DetectionStartedEvent(FileHash VideoFile, LocalPath Path) : VideoFileEvent(VideoFile);
