using Debarr.Scanning;

namespace Debarr.Detecting;

/// <summary>A video file the detection queue or Detect Now asks to detect.</summary>
/// <param name="Path">The video file's most recently hashed file path.</param>
public sealed record DetectionRequest(FileHash VideoFile, LocalPath Path, DetectionOrigin Origin);
