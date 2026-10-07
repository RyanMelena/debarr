using Debarr.Scanning;

namespace Debarr.Components;

/// <summary>A root folder as Settings &gt; Library and System &gt; Status list it: one in the library, or one whose removal runs after the library dropped it.</summary>
/// <param name="RootFolder">Null once the library has dropped the root folder.</param>
/// <param name="Scanning">Whether the running library scan has the root folder still to finish.</param>
/// <param name="Removal">The root folder's running removal; null while none runs.</param>
public sealed record RootFolderRow(string Path, RootFolder? RootFolder, RootFolderScanned? LastScan, bool Scanning, RootFolderRemovalStartedEvent? Removal)
{
    /// <summary>The library's root folders, then the root folder whose removal runs when the library no longer lists it.</summary>
    public static IReadOnlyList<RootFolderRow> Of(
        IReadOnlyList<(RootFolder RootFolder, RootFolderScanned? LastScan, bool Scanning)> rootFolders,
        RootFolderRemovalStartedEvent? removal)
    {
        List<RootFolderRow> rows =
        [
            .. rootFolders.Select(root => new RootFolderRow(
                root.RootFolder.Path.Value,
                root.RootFolder,
                root.LastScan,
                root.Scanning,
                removal?.RootFolder == root.RootFolder.Path.Value ? removal : null)),
        ];
        if (removal is not null && rows.All(row => row.Removal is null))
        {
            rows.Add(new RootFolderRow(removal.RootFolder, null, null, false, removal));
        }

        return rows;
    }
}
