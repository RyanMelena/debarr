using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public class FileInfoExtensionsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("debarr-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_readable_file_can_open()
    {
        var path = Write([.. "hello"u8]);

        Assert.True(new FileInfo(path).CanOpen());
    }

    [Fact]
    public void A_missing_file_cannot_open()
    {
        Assert.False(new FileInfo(Path.Combine(_directory, "missing.mkv")).CanOpen());
    }

    [Fact]
    public void A_file_held_open_without_sharing_cannot_open()
    {
        var path = Write([.. "hello"u8]);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.False(new FileInfo(path).CanOpen());
    }

    private string Write(byte[] content)
    {
        var path = Path.Combine(_directory, "video.mkv");
        File.WriteAllBytes(path, content);
        return path;
    }
}
