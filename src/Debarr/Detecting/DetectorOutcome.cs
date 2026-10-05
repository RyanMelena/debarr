using FluentResults;

namespace Debarr.Detecting;

/// <summary>What the detector read and found in one run, which a detection records.</summary>
/// <param name="FfmpegVersion">The version of the ffmpeg that ran; null when reading it failed.</param>
/// <param name="ContainerMetadata">What ffprobe read; null when the detection failed before or during ffprobe.</param>
/// <param name="Result">The detection result, or the failure's error messages.</param>
public sealed record DetectorOutcome(string? FfmpegVersion, ContainerMetadata? ContainerMetadata, Result<DetectionResult> Result);
