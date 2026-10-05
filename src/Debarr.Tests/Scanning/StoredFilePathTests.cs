using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher.Linq;

namespace Debarr.Tests.Scanning;

public sealed class StoredFilePathTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    private static readonly FileStat Written = new(1, Now.AddDays(-3));

    private static readonly FileStat Copied = new(1, Now.AddDays(-2));

    private static readonly FileStat Touched = new(1, Now.AddDays(-1));

    private static readonly string Movies = Path.Combine(Path.GetTempPath(), "movies");

    [Fact]
    public async Task Each_file_path_is_stored_with_its_video_file_until_it_is_removed()
    {
        var film = TestFileHash.For("film");
        await AppendAsync(
            film,
            new VideoFileDiscovered(film, 1, Now),
            new FilePathAdded(film, new LocalPath("/movies/Film.mkv"), Written, Now, Now),
            new FilePathAdded(film, new LocalPath("/backup/Film.mkv"), Copied, Now.AddMinutes(1), Now.AddMinutes(1)));

        Assert.Equal(
            [("/backup/Film.mkv", film, Copied), ("/movies/Film.mkv", film, Written)],
            await ReadRowsAsync());

        await AppendAsync(film, new FilePathRemoved(film, new LocalPath("/backup/Film.mkv"), Now.AddHours(1)));

        Assert.Equal([("/movies/Film.mkv", film, Written)], await ReadRowsAsync());
    }

    [Fact]
    public async Task A_path_hashed_again_takes_the_new_stat()
    {
        var film = TestFileHash.For("film");
        await AppendAsync(
            film,
            new VideoFileDiscovered(film, 1, Now),
            new FilePathAdded(film, new LocalPath("/movies/Film.mkv"), Written, Now, Now),
            new FilePathAdded(film, new LocalPath("/movies/Film.mkv"), Touched, Now, Now.AddDays(1)));

        Assert.Equal([("/movies/Film.mkv", film, Touched)], await ReadRowsAsync());
    }

    [Fact]
    public async Task A_path_whose_content_changed_moves_to_the_new_video_file_with_the_stat_it_was_hashed_at()
    {
        var original = TestFileHash.For("original");
        var reencoded = TestFileHash.For("reencoded");
        var reencodedStat = new FileStat(2, Now.AddDays(1));
        await AppendAsync(original, new VideoFileDiscovered(original, 1, Now), new FilePathAdded(original, new LocalPath("/movies/Film.mkv"), Written, Now, Now));
        await AppendAsync(original, new FilePathRemoved(original, new LocalPath("/movies/Film.mkv"), Now.AddDays(1)));
        await AppendAsync(reencoded, new VideoFileDiscovered(reencoded, 2, Now.AddDays(1)), new FilePathAdded(reencoded, new LocalPath("/movies/Film.mkv"), reencodedStat, Now.AddDays(1), Now.AddDays(1)));

        Assert.Equal([("/movies/Film.mkv", reencoded, reencodedStat)], await ReadRowsAsync());
    }

    [Fact]
    public async Task A_rebuild_replays_the_file_paths()
    {
        var film = TestFileHash.For("film");
        await AppendAsync(
            film,
            new VideoFileDiscovered(film, 1, Now),
            new FilePathAdded(film, new LocalPath("/movies/Film.mkv"), Written, Now, Now),
            new FilePathAdded(film, new LocalPath("/movies/Gone.mkv"), Written, Now, Now),
            new FilePathRemoved(film, new LocalPath("/movies/Gone.mkv"), Now));
        await using (var session = Store.LightweightSession())
        {
            var row = await session.LoadAsync<StoredFilePath>("/movies/Film.mkv", CancellationToken);
            row!.Stat = Touched;
            session.Store(row);
            await session.SaveChangesAsync(CancellationToken);
        }

        await RebuildAsync(StoredFilePathProjection.ReadModel);

        Assert.Equal([("/movies/Film.mkv", film, Written)], await ReadRowsAsync());
    }

    [Fact]
    public async Task The_paths_under_a_folder_are_read_with_their_video_file_and_stat_by_whole_path_segments()
    {
        var film = TestFileHash.For("film");
        var sibling = TestFileHash.For("sibling");
        await AppendAsync(
            film,
            new VideoFileDiscovered(film, 1, Now),
            new FilePathAdded(film, new LocalPath(Path.Combine(Movies, "Film.mkv")), Written, Now, Now),
            new FilePathAdded(film, new LocalPath(Path.Combine(Movies, "Sub", "Film.mkv")), Copied, Now, Now));
        await AppendAsync(
            sibling,
            new VideoFileDiscovered(sibling, 1, Now),
            new FilePathAdded(sibling, new LocalPath(Movies + "2" + Path.DirectorySeparatorChar + "Film.mkv"), Written, Now, Now),
            new FilePathAdded(sibling, new LocalPath(Movies + ".mkv"), Written, Now, Now));

        await using var session = Store.QuerySession();

        Assert.Equal(
            [
                (Path.Combine(Movies, "Film.mkv"), film, Written),
                (Path.Combine(Movies, "Sub", "Film.mkv"), film, Copied),
            ],
            (await session.ReadStoredUnderAsync(Movies, CancellationToken)).Select(stored => (stored.Id, stored.VideoFile, stored.Stat)).OrderBy(stored => stored.Id, StringComparer.Ordinal));
        Assert.Equal(2, await session.CountUnderAsync(Movies, CancellationToken));
        Assert.Equal(
            [Movies + ".mkv", Movies + "2" + Path.DirectorySeparatorChar + "Film.mkv"],
            (await session.ReadStoredOutsideAsync([Movies], CancellationToken)).Select(stored => stored.Id).Order(StringComparer.Ordinal));
        Assert.Equal(4, (await session.ReadStoredOutsideAsync([], CancellationToken)).Count);
    }

    [Fact]
    public async Task The_range_reads_name_the_table_fisher_stores_the_documents_in()
    {
        await using var session = Store.QuerySession();

        Assert.Equal(["data"], session.AdvancedSql.SelectFieldsFor<StoredFilePath>());
        Assert.Equal(
            [0L],
            await session.AdvancedSql.QueryAsync<long>($"select count(*) from {StoredFilePathQuery.TableName} where id = ?", CancellationToken, "none"));
    }

    private async Task AppendAsync(FileHash videoFile, params object[] events)
    {
        await TestVideoFile.AppendAsync(Store, videoFile, events, CancellationToken);
    }

    private async Task<List<(string Path, FileHash VideoFile, FileStat Stat)>> ReadRowsAsync()
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Query<StoredFilePath>().ToListAsync(CancellationToken))
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => (row.Id, row.VideoFile, row.Stat))];
    }
}
