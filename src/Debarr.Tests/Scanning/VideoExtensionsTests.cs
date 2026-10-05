using Debarr.Extensions;
using Debarr.Scanning;

namespace Debarr.Tests.Scanning;

public class VideoExtensionsTests
{
    [Fact]
    public void Parse_splits_on_spaces_or_commas_and_keeps_each_extension_once_lowercase_with_no_dot() =>
        Assert.Equal(["mkv", "mp4", "avi"], VideoExtensions.Parse(" .MKV, mp4\tmkv .Avi . ").Value.Extensions);

    [Theory]
    [InlineData("")]
    [InlineData(" , . ")]
    public void Parse_refuses_a_list_with_no_extension(string text) =>
        Assert.Equal(
            new Dictionary<string, string> { ["VideoExtensions"] = "Enter at least one extension." },
            VideoExtensions.Parse(text).GetFieldErrors());

    [Theory]
    [InlineData("Film.mkv", true)]
    [InlineData("Film.MKV", true)]
    [InlineData("Film.srt", false)]
    [InlineData("mkv", false)]
    public void A_file_is_a_video_file_when_its_extension_is_listed_ignoring_case(string name, bool included) =>
        Assert.Equal(included, VideoExtensions.Default.Includes(new FileInfo(name)));

    [Fact]
    public void Two_lists_of_the_same_extensions_in_the_same_order_are_equal()
    {
        Assert.Equal(VideoExtensions.Parse("mkv mp4").Value, VideoExtensions.Parse(".MKV,mp4").Value);
        Assert.NotEqual(VideoExtensions.Parse("mkv mp4").Value, VideoExtensions.Parse("mp4 mkv").Value);
    }

    [Fact]
    public void The_list_reads_as_its_extensions_separated_by_spaces() =>
        Assert.Equal("mkv mp4 m4v avi mov ts m2ts wmv webm mpg mpeg", VideoExtensions.Default.ToString());
}
