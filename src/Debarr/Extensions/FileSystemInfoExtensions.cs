namespace Debarr.Extensions;

public static class FileSystemInfoExtensions
{
    /// <summary>Whether the file or folder is the folder or under it, compared by whole path segments with the platform's case rules.</summary>
    public static bool IsSameOrUnder(this FileSystemInfo entry, DirectoryInfo folder)
    {
        var relativePath = Path.GetRelativePath(folder.FullName, entry.FullName);

        // A path on another volume comes back rooted.
        return relativePath != ".."
            && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }
}
