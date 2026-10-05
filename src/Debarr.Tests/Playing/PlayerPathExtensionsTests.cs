using Debarr.Playing;
using Debarr.Scanning;

namespace Debarr.Tests.Playing;

public class PlayerPathExtensionsTests
{
    private static readonly PathMapping Movies = Mapping("/movies", "/media/movies");

    private static readonly PathMapping Movies4k = Mapping("/movies/4k", "/media/uhd");

    [Theory]
    [InlineData("plugin://plugin.video.youtube/play/?video_id=abc", true)]
    [InlineData("PVR://channels/tv/All channels/1.pvr", true)]
    [InlineData("smb://nas/media/Movies/Arrival.mkv", false)]
    [InlineData("/movies/plugin://x.mkv", false)]
    public void A_plugin_or_pvr_path_is_a_stream(string path, bool isStream) =>
        Assert.Equal(isStream, new PlayerPath(path).IsStream());

    [Theory]
    [InlineData("/movies/Dune (2021)/Dune.mkv", "/media/movies/Dune (2021)/Dune.mkv")]
    [InlineData("/movies/4k/x.mkv", "/media/uhd/x.mkv")]
    [InlineData("/movies", "/media/movies")]
    [InlineData("/movies2/x.mkv", "/movies2/x.mkv")]
    [InlineData("/movies/4k2/x.mkv", "/media/movies/4k2/x.mkv")]
    public void The_longest_player_path_covering_whole_segments_translates_the_path(string path, string expected)
    {
        Assert.Equal(expected, Translate(path, [Movies, Movies4k]));
        Assert.Equal(expected, Translate(path, [Movies4k, Movies]));
    }

    [Fact]
    public void A_translated_path_joins_its_segments_with_the_local_directory_separator()
    {
        var separator = Path.DirectorySeparatorChar;

        Assert.Equal(
            $@"\\nas\media{separator}Film{separator}a.mkv",
            new PlayerPath("smb://nas/media/Film/a.mkv").ToLocalPath([Mapping("smb://nas/media", @"\\nas\media")]).Value);
    }

    [Fact]
    public void A_path_with_no_path_mappings_is_unchanged() =>
        Assert.Equal("smb://nas/media/a.mkv", Translate("smb://nas/media/a.mkv", []));

    [Fact]
    public void Trailing_separators_on_either_path_are_ignored() =>
        Assert.Equal(
            "/data/media/Film/a.mkv",
            Translate("smb://nas/media/Film/a.mkv", [Mapping("smb://nas/media/", "/data/media/")]));

    [Fact]
    public void A_root_player_path_covers_every_absolute_path() =>
        Assert.Equal("/data/movies/a.mkv", Translate("/movies/a.mkv", [Mapping("/", "/data")]));

    [Fact]
    public void A_root_local_path_keeps_one_separator() =>
        Assert.Equal("/movies/a.mkv", Translate("smb://nas/movies/a.mkv", [Mapping("smb://nas", "/")]));

    [Fact]
    public void A_player_path_is_compared_case_sensitively() =>
        Assert.Equal("/Movies/a.mkv", Translate("/Movies/a.mkv", [Movies]));

    /// <summary>The translated path with its separators written as forward slashes, for tests of which path mapping matches.</summary>
    private static string Translate(string path, IEnumerable<PathMapping> pathMappings) =>
        new PlayerPath(path).ToLocalPath(pathMappings).Value.Replace(Path.DirectorySeparatorChar, '/');

    private static PathMapping Mapping(string playerPath, string localPath) =>
        new(new PlayerPath(playerPath), new LocalPath(localPath));
}
