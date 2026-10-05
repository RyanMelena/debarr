using Debarr.EventStore;
using Debarr.Scanning;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Detecting;

/// <summary>Records a finished detection of a video file: what started it, the path it read, when and for how long, and what the detector found.</summary>
/// <param name="DetectionId">A version 7 Guid the detection runner generates.</param>
public sealed record RecordDetection(
    Guid DetectionId,
    FileHash VideoFile,
    DetectionOrigin Origin,
    LocalPath Path,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int DetectorVersion,
    DetectorOutcome Outcome) : IFilePathCommand
{
    /// <summary>The video file's stream, which Wolverine loads the aggregate from.</summary>
    public Guid VideoFileId => VideoFile.StreamId;
}

public static class RecordDetectionHandler
{
    /// <summary>
    /// A detection result, which becomes the current result and clears the last failure, or a failure, which leaves the current result in place.
    /// A video file that is gone or archived records nothing.
    /// </summary>
    public static IReadOnlyList<object> Handle(RecordDetection command, [WriteModel(Required = false)] VideoFile? videoFile)
    {
        if (videoFile is null or { Archived: true })
        {
            return [];
        }

        var outcome = command.Outcome;
        var detection = new Detection(
            command.DetectionId,
            command.Origin,
            command.Path,
            command.StartedAt,
            command.Duration,
            command.DetectorVersion,
            outcome.FfmpegVersion,
            outcome.ContainerMetadata,
            outcome.Result.IsSuccess ? new DetectionOutcome.Succeeded(outcome.Result.Value) : DetectionOutcome.Failed.From(outcome.Result.Errors));
        return [outcome.Result.IsSuccess ? new AspectRatioDetected(command.VideoFile, detection) : new DetectionFailed(command.VideoFile, detection)];
    }
}
