using Debarr.Detecting;
using Fisher;
using Wolverine.Fisher;

namespace Debarr.Scanning;

/// <summary>Archives each of the video files that has no file path left.</summary>
public sealed record ArchiveVideoFiles(IReadOnlyList<FileHash> VideoFiles, DateTimeOffset ArchivedAt);

public static class ArchiveVideoFilesHandler
{
    /// <summary>The Media rows of the command's live video files.</summary>
    public static Task<IReadOnlyList<MediaRow>> LoadAsync(ArchiveVideoFiles command, IQuerySession session, CancellationToken cancellationToken) =>
        session.LoadMediaRowsAsync(command.VideoFiles, cancellationToken);

    /// <summary>Archives each video file whose row has no file path, at the version the row was read at, in one transaction.</summary>
    public static IEnumerable<IFisherOp> Handle(ArchiveVideoFiles command, IReadOnlyList<MediaRow> mediaRows)
    {
        var archived = new VideoFileArchived(command.ArchivedAt);
        return mediaRows
            .Where(row => !row.HasFilePath)
            .SelectMany(row => new IFisherOp[] { FisherOps.Append(row.Id, row.Version, archived), FisherOps.ArchiveStream(row.Id) });
    }
}
