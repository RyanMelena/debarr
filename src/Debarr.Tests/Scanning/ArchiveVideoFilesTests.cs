using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Wolverine.Fisher;

namespace Debarr.Tests.Scanning;

public sealed class ArchiveVideoFilesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Each_video_file_whose_row_has_no_file_path_is_archived_at_the_version_its_row_was_read_at()
    {
        var left = Row("left", version: 3);
        var alsoLeft = Row("also-left", version: 7);

        var ops = ArchiveVideoFilesHandler.Handle(new ArchiveVideoFiles([left.FileHash, alsoLeft.FileHash], Now), [left, alsoLeft]).ToList();

        Assert.Equal(
            [(left.Id, 3L, typeof(AppendToStream)), (left.Id, 0L, typeof(ArchiveStream)), (alsoLeft.Id, 7L, typeof(AppendToStream)), (alsoLeft.Id, 0L, typeof(ArchiveStream))],
            ops.Select(op => op switch
            {
                AppendToStream append => (append.StreamId, append.ExpectedVersion ?? 0, typeof(AppendToStream)),
                ArchiveStream archive => (archive.StreamId, 0L, typeof(ArchiveStream)),
                _ => (Guid.Empty, 0L, op.GetType()),
            }));
        Assert.All(ops.OfType<AppendToStream>(), append => Assert.Equal([new VideoFileArchived(Now)], append.Events));
    }

    [Fact]
    public void A_video_file_with_a_file_path_or_with_no_row_is_left_as_it_is()
    {
        var kept = Row("kept", version: 2, path: "/movies/kept.mkv");

        Assert.Empty(ArchiveVideoFilesHandler.Handle(new ArchiveVideoFiles([kept.FileHash, TestFileHash.For("archived")], Now), [kept]));
    }

    private static MediaRow Row(string name, long version, string? path = null) => new()
    {
        Id = TestFileHash.For(name).StreamId,
        FileHash = TestFileHash.For(name),
        Version = version,
        FilePaths = path is null ? [] : [new FilePath(new LocalPath(path), TestVideoFile.Stat, Now, Now)],
    };
}
