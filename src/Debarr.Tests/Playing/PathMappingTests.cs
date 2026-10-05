using Debarr.Playing;
using Debarr.Scanning;

namespace Debarr.Tests.Playing;

public class PathMappingTests
{
    private static readonly PathMapping Movies = new(new PlayerPath("/movies"), new LocalPath("/media/movies"));

    [Theory]
    [InlineData("/movies2/x.mkv")]
    [InlineData("/tv/x.mkv")]
    [InlineData("/Movies/x.mkv")]
    public void A_path_the_player_path_doesnt_cover_by_whole_segments_has_no_translation(string path) =>
        Assert.Null(Movies.Translate(new PlayerPath(path)));

    [Fact]
    public void A_covered_path_keeps_the_segments_after_the_player_path() =>
        Assert.Equal(
            $"/media/movies{Path.DirectorySeparatorChar}Arrival{Path.DirectorySeparatorChar}a.mkv",
            Movies.Translate(new PlayerPath("/movies/Arrival/a.mkv"))?.Value);

    [Fact]
    public void The_player_path_itself_translates_to_the_local_path() =>
        Assert.Equal("/media/movies", Movies.Translate(new PlayerPath("/movies"))?.Value);
}
