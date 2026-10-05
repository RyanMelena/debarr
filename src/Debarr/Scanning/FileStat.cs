using System.Text.Json.Serialization;

namespace Debarr.Scanning;

/// <summary>
/// A file's size and modification time as a scan read them when it hashed the file, which tell whether the file changed since.
/// The modification time is UTC, truncated to whole milliseconds, so it compares exactly with a fresh stat and survives storage as text.
/// </summary>
public readonly record struct FileStat
{
    [JsonConstructor]
    public FileStat(long size, DateTimeOffset modifiedAt)
    {
        Size = size;
        ModifiedAt = DateTimeOffset.FromUnixTimeMilliseconds(modifiedAt.ToUnixTimeMilliseconds());
    }

    /// <summary>The size in bytes.</summary>
    public long Size { get; }

    public DateTimeOffset ModifiedAt { get; }

    /// <summary>The file's stat now.</summary>
    public static FileStat From(FileInfo file) => new(file.Length, new DateTimeOffset(file.LastWriteTimeUtc));

    /// <summary>Whether the file exists with this size and modification time.</summary>
    public bool Matches(FileInfo file) => file.Exists && From(file) == this;
}
