using Debarr.Detecting;
using Fisher;
using Fisher.Projections;
using JasperFx.Events.Projections;

namespace Debarr.Scanning;

/// <summary>A file path as a scan compares it with the disk: its video file, and its stat when a scan last hashed it.</summary>
public sealed class StoredFilePath
{
    /// <summary>The local path, which keys the document.</summary>
    public string Id { get; set; } = "";

    public FileHash VideoFile { get; set; }

    public FileStat Stat { get; set; }
}

/// <summary>Each event writes the whole document, so two commits that touch one path at once lose nothing.</summary>
public sealed class StoredFilePathProjection : MultiStreamProjection<StoredFilePath, string>
{
    public const string ReadModel = nameof(StoredFilePath);

    public StoredFilePathProjection()
    {
        Name = ReadModel;
        Identity<FilePathAdded>(added => added.Path.Value);
        Identity<FilePathRemoved>(removed => removed.Path.Value);
    }

    public static void AddTo(StoreOptions options) => options.Projections.Add(new StoredFilePathProjection(), ProjectionLifecycle.Inline);

    public static StoredFilePath Create(FilePathAdded added) => new() { Id = added.Path.Value, VideoFile = added.VideoFile, Stat = added.Stat };

    public static StoredFilePath Apply(FilePathAdded added, StoredFilePath storedFilePath) => Create(added);

    public static bool ShouldDelete(FilePathRemoved removed) => true;
}

/// <summary>Reads the file paths under a folder as a range of ids in SQL, which the table's primary key serves.</summary>
public static class StoredFilePathQuery
{
    public const string TableName = "fi_doc_storedfilepath";

    private const string SelectStored = $"select id, json_extract(data, '$.videoFile.value'), json_extract(data, '$.stat') from {TableName}";

    /// <summary>The stored file paths under the folder, compared by whole path segments.</summary>
    public static async Task<IReadOnlyList<StoredFilePath>> ReadStoredUnderAsync(this IQuerySession session, string folder, CancellationToken cancellationToken)
    {
        var (lowerBound, upperBound) = BoundsUnder(folder);
        return ToStored(await session.AdvancedSql.QueryAsync<string, string, FileStat>($"{SelectStored} where id >= ? and id < ?", cancellationToken, lowerBound, upperBound));
    }

    public static async Task<IReadOnlyList<StoredFilePath>> ReadStoredOutsideAsync(this IQuerySession session, IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        var bounds = folders.Select(BoundsUnder).ToList();
        var where = bounds.Count == 0 ? "" : " where " + string.Join(" and ", bounds.Select(_ => "(id < ? or id >= ?)"));
        object[] parameters = [.. bounds.SelectMany(bound => new object[] { bound.LowerBound, bound.UpperBound })];
        return ToStored(await session.AdvancedSql.QueryAsync<string, string, FileStat>(SelectStored + where, cancellationToken, parameters));
    }

    public static async Task<int> CountUnderAsync(this IQuerySession session, string folder, CancellationToken cancellationToken)
    {
        var (lowerBound, upperBound) = BoundsUnder(folder);
        var counts = await session.AdvancedSql.QueryAsync<long>($"select count(*) from {TableName} where id >= ? and id < ?", cancellationToken, lowerBound, upperBound);
        return (int)counts[0];
    }

    /// <summary>Every path that starts with the folder and a separator sorts from the lower bound up to the upper one.</summary>
    private static (string LowerBound, string UpperBound) BoundsUnder(string folder) =>
        (folder + Path.DirectorySeparatorChar, folder + (char)(Path.DirectorySeparatorChar + 1));

    private static List<StoredFilePath> ToStored(IEnumerable<(string Path, string VideoFile, FileStat Stat)> rows) =>
        [.. rows.Select(row => new StoredFilePath { Id = row.Path, VideoFile = new FileHash(row.VideoFile), Stat = row.Stat })];
}
