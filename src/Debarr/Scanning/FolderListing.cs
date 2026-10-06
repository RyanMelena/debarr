using FluentResults;

namespace Debarr.Scanning;

/// <summary>The folders inside one folder on Debarr's filesystem, or the filesystem's roots when <see cref="Folder"/> is null.</summary>
/// <param name="Folder">The canonical path of the folder listed, or null for the drives on Windows.</param>
/// <param name="Subfolders">The folder's visible direct subfolders, or the drives, sorted by name.</param>
public sealed record FolderListing(LocalPath? Folder, IReadOnlyList<LocalPath> Subfolders)
{
    private static readonly EnumerationOptions VisibleFolders = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        // A folder Debarr cannot read throws, so its listing fails with a reason.
        IgnoreInaccessible = false,
    };

    /// <summary>Whether a listing sits above this one: false at the top, which is the drives on Windows and <c>/</c> elsewhere.</summary>
    public bool CanGoUp => Folder is not null && (Up is not null || OperatingSystem.IsWindows());

    /// <summary>The parent folder, or null when the folder is a root, whose parent is the top.</summary>
    public LocalPath? Up => Folder is { } folder && Path.GetDirectoryName(folder.Value) is { } parent ? new LocalPath(parent) : null;

    /// <summary>Lists the folder, or the top when <paramref name="folder"/> is null, or says why Debarr cannot.</summary>
    public static Result<FolderListing> Read(LocalPath? folder)
    {
        if (folder is null)
        {
            return OperatingSystem.IsWindows() ? ReadDrives() : Read(new LocalPath("/"));
        }

        var directory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Value.Value)));
        if (!directory.Exists)
        {
            return Result.Fail($"Debarr cannot find the folder {directory.FullName}.");
        }

        try
        {
            var subfolders = directory.EnumerateDirectories("*", VisibleFolders)
                .OrderBy(subfolder => subfolder.Name, StringComparer.OrdinalIgnoreCase)
                .Select(subfolder => new LocalPath(subfolder.FullName))
                .ToList();
            return new FolderListing(new LocalPath(directory.FullName), subfolders);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return Result.Fail($"Debarr cannot read the folder {directory.FullName}.");
        }
    }

    private static FolderListing ReadDrives() => new(
        null,
        [.. DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new LocalPath(path))]);
}
