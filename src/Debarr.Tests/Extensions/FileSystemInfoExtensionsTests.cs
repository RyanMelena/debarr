using Debarr.Extensions;

namespace Debarr.Tests.Extensions;

public class FileSystemInfoExtensionsTests
{
    private static readonly string Movies = Path.Combine(Path.GetTempPath(), "movies");

    [Fact]
    public void A_folder_is_same_or_under_itself() =>
        Assert.True(new DirectoryInfo(Movies).IsSameOrUnder(new DirectoryInfo(Movies)));

    [Fact]
    public void A_trailing_separator_names_the_same_folder() =>
        Assert.True(new DirectoryInfo(Movies).IsSameOrUnder(new DirectoryInfo(Movies + Path.DirectorySeparatorChar)));

    [Fact]
    public void A_file_in_a_subfolder_is_under_the_folder() =>
        Assert.True(new FileInfo(Path.Combine(Movies, "sub", "a.mkv")).IsSameOrUnder(new DirectoryInfo(Movies)));

    [Fact]
    public void A_file_named_with_leading_dots_is_under_the_folder() =>
        Assert.True(new FileInfo(Path.Combine(Movies, "..a.mkv")).IsSameOrUnder(new DirectoryInfo(Movies)));

    [Fact]
    public void A_sibling_sharing_the_folder_name_as_a_prefix_is_outside_it() =>
        Assert.False(new DirectoryInfo(Movies + "2").IsSameOrUnder(new DirectoryInfo(Movies)));

    [Fact]
    public void The_parent_folder_is_outside_it() =>
        Assert.False(new DirectoryInfo(Path.GetTempPath()).IsSameOrUnder(new DirectoryInfo(Movies)));

    [Fact]
    public void A_path_that_climbs_out_of_the_folder_is_outside_it() =>
        Assert.False(new FileInfo(Path.Combine(Movies, "..", "tv", "a.mkv")).IsSameOrUnder(new DirectoryInfo(Movies)));
}
