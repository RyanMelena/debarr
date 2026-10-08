using Debarr.Playing;
using Debarr.Scanning;

namespace Debarr.Tests.Playing;

public sealed class PlayerTests
{
    private static readonly Player Theater = new(
        Guid.NewGuid(),
        "Theater",
        true,
        KodiEndpoint.Create("kodi.lan", 9090, 60, 10).Value,
        [new PathMapping(new PlayerPath("/media"), new LocalPath("/mnt/media")), new PathMapping(new PlayerPath("/media/private/shared"), new LocalPath("/mnt/shared"))],
        [new PlayerPath("/media/private"), new PlayerPath("/home")]);

    [Theory]
    [InlineData("/media/private/Secret.mkv", true)]
    [InlineData("/media/private", true)]
    [InlineData("/home/videos/Clip.mkv", true)]
    [InlineData("/media/movies/Arrival.mkv", false)]
    [InlineData("/media/privateer/Film.mkv", false)]
    [InlineData("/media/private/shared/Film.mkv", false)]
    [InlineData("/other/Film.mkv", false)]
    public void The_longest_player_path_covering_whole_segments_decides_whether_a_path_is_excluded(string path, bool excluded) =>
        Assert.Equal(excluded, Theater.Excludes(new PlayerPath(path)));
}
