using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public sealed class Int64ExtensionsTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(2411724L, "2.3 MB")]
    [InlineData(1181116006L, "1.1 GB")]
    public void The_file_size_text_uses_1024_based_units(long bytes, string expected) =>
        Assert.Equal(expected, bytes.ToFileSizeText());
}
