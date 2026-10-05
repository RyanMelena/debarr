using Debarr.Detecting;
using Fisher;
using Wolverine.Fisher;

namespace Debarr.Scanning;

/// <summary>Records that a scan, or a root folder's removal, dropped many file paths at once, each from the video file its stored file path names.</summary>
public sealed record RemoveFilePaths(IReadOnlyList<RemoveFilePath> Removals);

public static class RemoveFilePathsHandler
{
    /// <summary>The Media rows of the removals' live video files.</summary>
    public static Task<IReadOnlyList<MediaRow>> LoadAsync(RemoveFilePaths command, IQuerySession session, CancellationToken cancellationToken) =>
        session.LoadMediaRowsAsync(command.Removals.Select(removal => removal.VideoFile), cancellationToken);

    /// <summary>Removes each path its live video file's row still holds, at the version the row was read at, in one transaction.</summary>
    public static IEnumerable<IFisherOp> Handle(RemoveFilePaths command, IReadOnlyList<MediaRow> videoFiles)
    {
        var rows = videoFiles.ToDictionary(row => row.FileHash);
        return command.Removals
            .Where(removal => rows.TryGetValue(removal.VideoFile, out var row) && row.FilePaths.Any(filePath => filePath.Path == removal.Path))
            .Select(removal => FisherOps.Append(removal.VideoFileId, rows[removal.VideoFile].Version, new FilePathRemoved(removal.VideoFile, removal.Path, removal.RemovedAt)));
    }
}
