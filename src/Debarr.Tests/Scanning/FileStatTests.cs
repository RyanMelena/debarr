using Debarr.Scanning;

namespace Debarr.Tests.Scanning;

public sealed class FileStatTests : IDisposable
{
    private static readonly DateTime WrittenAt = new DateTime(2026, 9, 30, 20, 15, 0, 123, DateTimeKind.Utc).AddTicks(4_567);

    private readonly string _directory = Directory.CreateTempSubdirectory("debarr-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_files_stat_is_its_size_and_its_modification_time_in_utc_truncated_to_milliseconds()
    {
        var stat = FileStat.From(new FileInfo(Write("film.mkv", 5)));

        Assert.Equal(5, stat.Size);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero).UtcTicks, stat.ModifiedAt.UtcTicks);
        Assert.Equal(TimeSpan.Zero, stat.ModifiedAt.Offset);
    }

    [Fact]
    public void A_stat_holds_its_modification_time_in_utc_truncated_to_milliseconds()
    {
        var stat = new FileStat(5, new DateTimeOffset(2026, 9, 30, 15, 15, 0, 123, TimeSpan.FromHours(-5)).AddTicks(9_999));

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero).UtcTicks, stat.ModifiedAt.UtcTicks);
        Assert.Equal(TimeSpan.Zero, stat.ModifiedAt.Offset);
    }

    [Fact]
    public void An_unchanged_file_matches_its_stat()
    {
        var path = Write("film.mkv", 5);
        var stat = FileStat.From(new FileInfo(path));

        Assert.True(stat.Matches(new FileInfo(path)));
    }

    [Fact]
    public void A_file_whose_size_changed_no_longer_matches()
    {
        var path = Write("film.mkv", 5);
        var stat = FileStat.From(new FileInfo(path));
        File.WriteAllBytes(path, new byte[6]);
        File.SetLastWriteTimeUtc(path, WrittenAt);

        Assert.False(stat.Matches(new FileInfo(path)));
    }

    [Fact]
    public void A_file_whose_modification_time_changed_no_longer_matches()
    {
        var path = Write("film.mkv", 5);
        var stat = FileStat.From(new FileInfo(path));
        File.SetLastWriteTimeUtc(path, WrittenAt.AddMilliseconds(1));

        Assert.False(stat.Matches(new FileInfo(path)));
    }

    [Fact]
    public void A_missing_file_does_not_match()
    {
        var path = Write("film.mkv", 5);
        var stat = FileStat.From(new FileInfo(path));
        File.Delete(path);

        Assert.False(stat.Matches(new FileInfo(path)));
    }

    /// <summary>Writes a file of <paramref name="length"/> bytes last written at <see cref="WrittenAt"/>.</summary>
    private string Write(string name, int length)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, new byte[length]);
        File.SetLastWriteTimeUtc(path, WrittenAt);
        return path;
    }
}
