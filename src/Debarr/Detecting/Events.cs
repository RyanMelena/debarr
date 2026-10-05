using Debarr.Scanning;

namespace Debarr.Detecting;

/// <param name="FirstSeenAt">When a scan first hashed the file, which orders the detection queue.</param>
public sealed record VideoFileDiscovered(FileHash FileHash, long Size, DateTimeOffset FirstSeenAt);

/// <summary>A scan hashed the file at a path to the video file: a new path, or a known one whose stat changed.</summary>
/// <param name="Stat">The file's stat when the scan hashed the path.</param>
/// <param name="FirstSeenAt">When a scan first found the path.</param>
/// <param name="HashedAt">When the scan hashed the path.</param>
public sealed record FilePathAdded(FileHash VideoFile, LocalPath Path, FileStat Stat, DateTimeOffset FirstSeenAt, DateTimeOffset HashedAt);

public sealed record FilePathRemoved(FileHash VideoFile, LocalPath Path, DateTimeOffset RemovedAt);

/// <summary>A detection found a result, which becomes the current result and clears the last failure.</summary>
public sealed record AspectRatioDetected(FileHash VideoFile, Detection Detection);

/// <summary>A detection failed, which becomes the last failure beside the current result.</summary>
public sealed record DetectionFailed(FileHash VideoFile, Detection Detection);

/// <summary>A standard ratios change replaced a detected result with a result from the file, in a detection of its own.</summary>
public sealed record DetectionResultConverted(FileHash VideoFile, Detection Detection);

/// <summary>The current result and the last failure were cleared, so the video file rejoins the detection queue.</summary>
public sealed record DetectionResultCleared(DateTimeOffset ClearedAt);

public sealed record OverrideSaved(Override Override);

/// <summary>The video file left the library with no file path left, keeping its result, override and detections.</summary>
public sealed record VideoFileArchived(DateTimeOffset ArchivedAt);

/// <summary>A scan found an archived video file's hash again, which brings it back with the state it was archived with.</summary>
/// <param name="FirstSeenAt">When a scan first hashed the file, before it was archived.</param>
public sealed record VideoFileRestored(
    FileHash FileHash,
    long Size,
    DateTimeOffset FirstSeenAt,
    Detection? CurrentResult,
    Detection? LastFailure,
    Override? Override,
    DateTimeOffset RestoredAt);

public sealed record DetectionSettingsChanged(int SimultaneousDetections, PictureMeasurement PictureMeasurement, int TimeoutSeconds);

/// <summary>The standard ratios or the match tolerance changed, and which current results that re-checks.</summary>
public sealed record StandardRatiosChanged(StandardRatios StandardRatios, RecheckScope Recheck);
