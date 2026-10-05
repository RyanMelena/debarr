using Debarr.Scanning;

namespace Debarr.Detecting;

/// <summary>A detection in progress: the video file, its most recently hashed file path, what asked for it, and when its slot was allocated.</summary>
public sealed record RunningDetection(FileHash VideoFile, LocalPath Path, DetectionOrigin Origin, DateTimeOffset StartedAt);
