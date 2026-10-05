using Wolverine.Persistence.EventSourcing;

namespace Debarr.Scanning;

/// <summary>Records that the library scan covered a root folder, as the root folder finishes.</summary>
/// <param name="Error">The scan's error, such as a missing folder; null when it read the root folder.</param>
public sealed record RecordRootFolderScan(Guid LibraryScanId, LocalPath Path, string? Error)
{
    /// <summary>The library's stream, which Wolverine reads the root folders from.</summary>
    public Guid LibraryId => Library.StreamId;
}

public static class RecordRootFolderScanHandler
{
    /// <summary>Records the root folder's scan while the library scan is open and the root folder is in the library.</summary>
    public static IReadOnlyList<object> Handle(
        RecordRootFolderScan command,
        [WriteModel(Required = false)] LibraryScan? libraryScan,
        [ReadModel(Required = false)] Library? library) =>
        libraryScan is { Ended: false } && library?.FindRootFolder(command.Path) is not null
            ? [new RootFolderScanned(command.Path, libraryScan.StartedAt, command.Error)]
            : [];
}
