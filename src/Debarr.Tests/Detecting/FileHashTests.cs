using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public class FileHashTests : IDisposable
{
    private const int MiB = 1024 * 1024;

    // SHA-256 of the size as a little-endian int64, then the first and last MiB, of a 3 MiB file whose byte i is i % 251.
    private const string ThreeMiBHash = "cc7c45908100c9f11f7496565787dd4daadd2b8e2380432a613b30f70ebc7045";

    private readonly string _directory = Directory.CreateTempSubdirectory("debarr-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("")]
    [InlineData("cc7c45908100c9f11f7496565787dd4daadd2b8e2380432a613b30f70ebc704")]
    [InlineData("CC7C45908100C9F11F7496565787DD4DAADD2B8E2380432A613B30F70EBC7045")]
    [InlineData("zz7c45908100c9f11f7496565787dd4daadd2b8e2380432a613b30f70ebc7045")]
    public void A_file_hash_is_64_lowercase_hex_digits(string value) =>
        Assert.Throws<ArgumentException>(() => new FileHash(value));

    [Fact]
    public void Two_file_hashes_of_the_same_digits_are_equal() =>
        Assert.Equal(new FileHash(ThreeMiBHash), new FileHash(ThreeMiBHash));

    [Fact]
    public async Task A_small_file_hashes_its_size_and_whole_content()
    {
        var path = Write([.. "hello"u8]);

        var fileHash = await FileHash.ComputeAsync(new FileInfo(path), TestContext.Current.CancellationToken);

        Assert.Equal("fe745503750fdbf3e6ef676d16d85ee0d63626c594222f7e991908bdffef7ac9", fileHash.Value);
    }

    [Fact]
    public async Task A_large_file_hashes_its_size_and_first_and_last_MiB()
    {
        var path = Write(Pattern(3 * MiB));

        var fileHash = await FileHash.ComputeAsync(new FileInfo(path), TestContext.Current.CancellationToken);

        Assert.Equal(ThreeMiBHash, fileHash.Value);
    }

    [Fact]
    public async Task A_copy_has_the_same_hash()
    {
        var original = Write(Pattern(3 * MiB));
        var copy = Path.Combine(_directory, "copy.mkv");
        File.Copy(original, copy);

        var fileHash = await FileHash.ComputeAsync(new FileInfo(copy), TestContext.Current.CancellationToken);

        Assert.Equal(ThreeMiBHash, fileHash.Value);
    }

    [Theory]
    [InlineData(MiB - 1, "7042e60eaccd2f73285fefdd55afa7d269e5fab0014e1a0a61fb5d89d2a70268")]
    [InlineData(2 * MiB, "8c3dd4a9f9886bdd4bb0086e0a8f5952b5c121da91721a690156538d30427f4f")]
    [InlineData(MiB + MiB / 2, ThreeMiBHash)]
    public async Task A_changed_byte_changes_the_hash_only_inside_a_sampled_chunk(int changedOffset, string expectedHash)
    {
        var content = Pattern(3 * MiB);
        content[changedOffset] ^= 0xFF;
        var path = Write(content);

        var fileHash = await FileHash.ComputeAsync(new FileInfo(path), TestContext.Current.CancellationToken);

        Assert.Equal(expectedHash, fileHash.Value);
    }

    [Theory]
    [InlineData(null, "74717985989787c169e0f50c3e23ff502a9e0651a6371857187389f1e1c9e995")]
    [InlineData(MiB, "c90aa756f922dd93e0250ae7fc70448c32f0fe5e1fe4ff3c87c0566439ba4a5d")]
    public async Task A_file_under_2_MiB_hashes_whole(int? changedOffset, string expectedHash)
    {
        var content = Pattern(2 * MiB - 1);
        if (changedOffset is { } offset)
        {
            content[offset] ^= 0xFF;
        }

        var path = Write(content);

        var fileHash = await FileHash.ComputeAsync(new FileInfo(path), TestContext.Current.CancellationToken);

        Assert.Equal(expectedHash, fileHash.Value);
    }

    private static byte[] Pattern(int length) => [.. Enumerable.Range(0, length).Select(index => (byte)(index % 251))];

    private string Write(byte[] content)
    {
        var path = Path.Combine(_directory, "video.mkv");
        File.WriteAllBytes(path, content);
        return path;
    }
}
