using Debarr.Detecting;
using Debarr.EventStore;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <summary>Records that a scan hashed the file at a path to the video file, as it found it now.</summary>
/// <param name="Stat">The file's stat when the scan hashed the path.</param>
/// <param name="HashedAt">When the scan hashed the path.</param>
public sealed record AddFilePath(FileHash VideoFile, LocalPath Path, FileStat Stat, DateTimeOffset HashedAt) : IFilePathCommand
{
    /// <summary>The video file's stream, which Wolverine loads the aggregate from.</summary>
    public Guid VideoFileId => VideoFile.StreamId;
}

public static class AddFilePathHandler
{
    /// <summary>
    /// Discovers a video file the first time its hash is seen, restores an archived one with the state it was archived with,
    /// adds a new path with a new first-seen time, records a changed stat on a known path, and does nothing for a path already recorded with its stat.
    /// </summary>
    public static IReadOnlyList<object> Handle(AddFilePath command, [WriteModel(Required = false)] VideoFile? videoFile)
    {
        var newPath = new FilePathAdded(command.VideoFile, command.Path, command.Stat, command.HashedAt, command.HashedAt);
        return (videoFile, videoFile?.FindFilePath(command.Path)) switch
        {
            (null, _) => [new VideoFileDiscovered(command.VideoFile, command.Stat.Size, command.HashedAt), newPath],
            ({ Archived: true } archived, _) =>
            [
                new VideoFileRestored(
                    archived.FileHash,
                    archived.Size,
                    archived.FirstSeenAt,
                    archived.CurrentResult,
                    archived.LastFailure,
                    archived.Override,
                    command.HashedAt),
                newPath,
            ],
            (_, null) => [newPath],
            (_, { Stat: var stat }) when stat == command.Stat => [],
            (_, { FirstSeenAt: var firstSeenAt }) => [new FilePathAdded(command.VideoFile, command.Path, command.Stat, firstSeenAt, command.HashedAt)],
        };
    }
}
