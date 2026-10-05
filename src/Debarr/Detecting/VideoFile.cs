using System.Text.Json.Serialization;
using Debarr.Scanning;
using Fisher;
using FluentResults;

namespace Debarr.Detecting;

/// <summary>
/// The content one file hash identifies, wherever it is stored: its size, its file paths, its detections,
/// its current result, its last failure and its override.
/// </summary>
/// <param name="FirstSeenAt">When a scan first hashed the file.</param>
/// <param name="FilePaths">Most recently hashed first, then by path, the order a detection tries them in.</param>
/// <param name="CurrentResult">The detection whose result a playback uses; null until one succeeds.</param>
/// <param name="LastFailure">The last failed detection since the current result; null when there is none.</param>
/// <param name="Archived">True while the last archive or restore on the stream is an archive.</param>
public sealed record VideoFile(
    FileHash FileHash,
    long Size,
    DateTimeOffset FirstSeenAt,
    IReadOnlyList<FilePath> FilePaths,
    IReadOnlyList<Detection> Detections,
    Detection? CurrentResult,
    Detection? LastFailure,
    Override? Override,
    bool Archived)
{
    /// <summary>The stream's id, which the event store keys the aggregate on.</summary>
    public Guid Id => FileHash.StreamId;

    /// <summary>The stream's version, which the event store writes, and which a command that appends to many streams expects.</summary>
    public long Version { get; init; }

    public static async Task<VideoFile?> ReadAsync(IQuerySession session, FileHash fileHash, CancellationToken cancellationToken) =>
        await session.Events.FetchLatest<VideoFile>(fileHash.StreamId, cancellationToken);

    public VideoFileStatus Status => StatusOf(Override, CurrentResult?.Result?.AspectRatioSource, LastFailure is not null);

    /// <summary>Manual with an override that has a ratio or Don't Send, otherwise the current result's source, otherwise Failed with a last failure, otherwise Pending.</summary>
    /// <param name="currentResultSource">Null when the video file has no current result.</param>
    public static VideoFileStatus StatusOf(Override? @override, AspectRatioSource? currentResultSource, bool hasLastFailure) =>
        (@override, currentResultSource, hasLastFailure) switch
        {
            ({ IsManual: true }, _, _) => VideoFileStatus.Manual,
            (_, AspectRatioSource.Detected, _) => VideoFileStatus.Detected,
            (_, not null, _) => VideoFileStatus.FromFile,
            (_, _, true) => VideoFileStatus.Failed,
            _ => VideoFileStatus.Pending,
        };

    public FilePath? FindFilePath(LocalPath path) => FilePaths.FirstOrDefault(filePath => filePath.Path == path);

    public DetectionRequest? ToDetectionRequest(DetectionOrigin origin) => DetectionRequestFor(FileHash, FilePaths, origin);

    /// <summary>A request to detect the video file from its most recently hashed path; null with no file path.</summary>
    /// <param name="filePaths">In detection order.</param>
    public static DetectionRequest? DetectionRequestFor(FileHash fileHash, IReadOnlyList<FilePath> filePaths, DetectionOrigin origin) =>
        filePaths is [var newest, ..] ? new DetectionRequest(fileHash, newest.Path, origin) : null;

    public static List<FilePath> InDetectionOrder(IEnumerable<FilePath> filePaths) =>
        [.. filePaths.OrderByDescending(filePath => filePath.HashedAt).ThenBy(filePath => filePath.Path.Value, StringComparer.Ordinal)];

    public static List<FilePath> WithFilePath(IEnumerable<FilePath> filePaths, FilePathAdded added) =>
        InDetectionOrder(filePaths.Where(filePath => filePath.Path != added.Path).Append(new FilePath(added.Path, added.Stat, added.FirstSeenAt, added.HashedAt)));

    public static List<FilePath> WithoutFilePath(IEnumerable<FilePath> filePaths, LocalPath path) => [.. filePaths.Where(filePath => filePath.Path != path)];

    public static VideoFile Create(VideoFileDiscovered discovered) =>
        new(discovered.FileHash, discovered.Size, discovered.FirstSeenAt, [], [], null, null, null, false);

    public VideoFile Apply(FilePathAdded added) => this with { FilePaths = WithFilePath(FilePaths, added) };

    public VideoFile Apply(FilePathRemoved removed) => this with { FilePaths = WithoutFilePath(FilePaths, removed.Path) };

    public VideoFile Apply(AspectRatioDetected detected) =>
        this with { Detections = [.. Detections, detected.Detection], CurrentResult = detected.Detection, LastFailure = null };

    public VideoFile Apply(DetectionFailed failed) => this with { Detections = [.. Detections, failed.Detection], LastFailure = failed.Detection };

    public VideoFile Apply(DetectionResultConverted converted) =>
        this with { Detections = [.. Detections, converted.Detection], CurrentResult = converted.Detection, LastFailure = null };

    public VideoFile Apply(DetectionResultCleared cleared) => this with { CurrentResult = null, LastFailure = null };

    public VideoFile Apply(OverrideSaved saved) => this with { Override = saved.Override };

    public VideoFile Apply(VideoFileArchived archived) => this with { Archived = true };

    public VideoFile Apply(VideoFileRestored restored) => this with { Archived = false };
}

/// <summary>One path under a root folder to a video file.</summary>
/// <param name="Stat">The file's stat when a scan last hashed the path.</param>
/// <param name="FirstSeenAt">When a scan first found the path.</param>
/// <param name="HashedAt">When a scan last hashed the path.</param>
public sealed record FilePath(LocalPath Path, FileStat Stat, DateTimeOffset FirstSeenAt, DateTimeOffset HashedAt);

/// <summary>
/// One finished detection: what started it, the path it read, when it started and how long it took, the versions that ran,
/// the container metadata when ffprobe read it, and its detection result or why it failed.
/// </summary>
/// <param name="Id">A version 7 Guid, which the sender of the command that records it generates.</param>
/// <param name="Path">The file path it read; null for a standard ratios change.</param>
public sealed record Detection(
    Guid Id,
    DetectionOrigin Origin,
    LocalPath? Path,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int DetectorVersion,
    string? FfmpegVersion,
    ContainerMetadata? ContainerMetadata,
    DetectionOutcome Outcome)
{
    [JsonIgnore]
    public DetectionResult? Result => (Outcome as DetectionOutcome.Succeeded)?.Result;

    [JsonIgnore]
    public string? Error => (Outcome as DetectionOutcome.Failed)?.Error;
}

/// <summary>A detection's result, or why it failed.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Succeeded), "succeeded")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record DetectionOutcome
{
    private DetectionOutcome()
    {
    }

    public sealed record Succeeded(DetectionResult Result) : DetectionOutcome;

    /// <param name="Error">Why the detection failed, in the detector's words.</param>
    public sealed record Failed(string Error) : DetectionOutcome
    {
        /// <summary>A failure with the errors' messages joined into one.</summary>
        public static Failed From(IEnumerable<IError> errors) => new(string.Join("; ", errors.Select(error => error.Message)));
    }
}
