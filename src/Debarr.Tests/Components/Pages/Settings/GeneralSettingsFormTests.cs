using Debarr.Components.Pages.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Debarr.Tests.Components.Pages.Settings;

public class GeneralSettingsFormTests
{
    [Fact]
    public void Get_changes_returns_only_the_changed_keys()
    {
        var original = CreateForm();
        var edited = original.Copy();
        edited.Port = 8181;
        edited.LogLevel = LogLevel.Debug;
        edited.FfmpegPath = "/usr/bin/ffmpeg";

        var changes = edited.GetChanges(original);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Server:Port"] = "8181",
                ["Logging:LogLevel:Default"] = "\"Debug\"",
                ["Ffmpeg:FfmpegPath"] = "\"/usr/bin/ffmpeg\"",
            },
            changes.ToDictionary(change => change.Key, change => change.Value.ToJsonString()));
    }

    [Fact]
    public void Get_changes_trims_text()
    {
        var original = CreateForm();
        var edited = original.Copy();
        edited.BindAddress = "  127.0.0.1 ";
        edited.FfprobePath = "ffprobe ";

        var changes = edited.GetChanges(original);

        var change = Assert.Single(changes);
        Assert.Equal("Server:BindAddress", change.Key);
        Assert.Equal("\"127.0.0.1\"", change.Value.ToJsonString());
    }

    [Fact]
    public void Get_changes_compares_and_saves_the_normalized_url_base()
    {
        var original = CreateForm();
        original.UrlBase = "/movies";
        var same = original.Copy();
        same.UrlBase = "movies/";
        var changed = original.Copy();
        changed.UrlBase = "films/";

        Assert.Empty(same.GetChanges(original));
        var change = Assert.Single(changed.GetChanges(original));
        Assert.Equal("Server:UrlBase", change.Key);
        Assert.Equal("\"/films\"", change.Value.ToJsonString());
    }

    [Fact]
    public void From_configuration_uses_the_option_defaults_for_an_empty_configuration()
    {
        var form = GeneralSettingsForm.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal("*", form.BindAddress);
        Assert.Equal(8080, form.Port);
        Assert.Equal("", form.UrlBase);
        Assert.Equal(LogLevel.Information, form.LogLevel);
        Assert.Equal("ffmpeg", form.FfmpegPath);
        Assert.Equal("ffprobe", form.FfprobePath);
    }

    [Fact]
    public void From_configuration_reads_each_key()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Server:BindAddress"] = "127.0.0.1",
                ["Server:Port"] = "8181",
                ["Server:UrlBase"] = "movies/",
                ["Logging:LogLevel:Default"] = "warning",
                ["Ffmpeg:FfmpegPath"] = "/usr/bin/ffmpeg",
                ["Ffmpeg:FfprobePath"] = "/usr/bin/ffprobe",
            })
            .Build();

        var form = GeneralSettingsForm.FromConfiguration(configuration);

        Assert.Equal("127.0.0.1", form.BindAddress);
        Assert.Equal(8181, form.Port);
        Assert.Equal("/movies", form.UrlBase);
        Assert.Equal(LogLevel.Warning, form.LogLevel);
        Assert.Equal("/usr/bin/ffmpeg", form.FfmpegPath);
        Assert.Equal("/usr/bin/ffprobe", form.FfprobePath);
    }

    private static GeneralSettingsForm CreateForm() =>
        GeneralSettingsForm.FromConfiguration(new ConfigurationBuilder().Build());
}
