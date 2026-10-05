using Debarr.Detecting;
using Debarr.EventStore;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <summary>Records that a scan no longer found the video file at a path.</summary>
public sealed record RemoveFilePath(FileHash VideoFile, LocalPath Path, DateTimeOffset RemovedAt) : IFilePathCommand
{
    /// <summary>The video file's stream, which Wolverine loads the aggregate from.</summary>
    public Guid VideoFileId => VideoFile.StreamId;
}

public static class RemoveFilePathHandler
{
    /// <summary>Removes the path, and does nothing when the video file has no such path.</summary>
    public static IReadOnlyList<object> Handle(RemoveFilePath command, [WriteModel(Required = false)] VideoFile? videoFile) =>
        videoFile?.FindFilePath(command.Path) is null ? [] : [new FilePathRemoved(command.VideoFile, command.Path, command.RemovedAt)];
}
