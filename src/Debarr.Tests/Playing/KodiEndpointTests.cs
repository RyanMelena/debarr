using Debarr.Playing;

namespace Debarr.Tests.Playing;

public class KodiEndpointTests
{
    [Theory]
    [InlineData("/media/movies/Dune (2021)/Dune.mkv", "/media/movies/Dune (2021)/Dune.mkv")]
    [InlineData("smb://nas/media/My%20Film/a.mkv", "smb://nas/media/My Film/a.mkv")]
    [InlineData("stack://smb://nas/a/cd1.mkv , smb://nas/a/cd2.mkv", "smb://nas/a/cd1.mkv")]
    [InlineData("stack://stack://smb://nas/a/cd1.mkv , smb://nas/a/cd2.mkv , smb://nas/a/cd3.mkv", "smb://nas/a/cd1.mkv")]
    [InlineData(@"D:\a\b//c/", "D:/a/b/c")]
    [InlineData(@"/media/music/AC\DC Live (2011)/AC\DC.mkv", @"/media/music/AC\DC Live (2011)/AC\DC.mkv")]
    [InlineData(@"smb://nas/media/music/AC\DC.mkv", @"smb://nas/media/music/AC\DC.mkv")]
    [InlineData("smb://nas/a//b/", "smb://nas/a/b")]
    [InlineData("file:///media/movies/a.mkv", "file:///media/movies/a.mkv")]
    [InlineData(@"\\nas\media\Film\a.mkv", "//nas/media/Film/a.mkv")]
    [InlineData("//nas/media//Film/a.mkv/", "//nas/media/Film/a.mkv")]
    [InlineData("/", "/")]
    [InlineData("caf\u0065\u0301.mkv", "caf\u00e9.mkv")]
    public void A_kodi_path_is_canonicalised(string path, string expected) =>
        Assert.Equal(expected, KodiEndpoint.Create("kodi.lan", 9090, 60, 10).Value.ToPlayerPath(path).Value);
}
