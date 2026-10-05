using Debarr.Tests.Extensions;
using System.Text.Json.Nodes;
using Bunit;
using Debarr.Components.Pages.Settings;
using Debarr.Hosting;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class GeneralPageTests : PageTestContext
{
    [Fact]
    public void The_page_says_its_settings_apply_on_restart_and_what_each_path_is()
    {
        var cut = RenderPage<GeneralPage>();

        cut.WaitForElement("#general-restart-help", Timeout);
        Assert.Contains("* listens on every address", cut.Markup);
        Assert.Contains("reverse proxy", cut.Markup);
        Assert.Contains("The ffmpeg that measures the picture", cut.Markup);
        Assert.Contains("The ffprobe that reads the container ratio", cut.Markup);
    }

    [Fact]
    public async Task A_save_that_fails_shows_why_and_keeps_the_edits()
    {
        // A folder where config.json goes makes the write fail.
        Directory.CreateDirectory(Path.Combine(DataDirectory, "config.json"));
        var cut = RenderPage<GeneralPage>();
        await cut.RaiseInputAsync("#general-url-base", "/debarr", Timeout);
        cut.WaitForAssertion(() => Assert.False(cut.Find("#general-save").HasAttribute("disabled")), Timeout);

        await cut.RaiseClickAsync("#general-save", Timeout);

        cut.WaitForAssertion(() => Assert.StartsWith("The settings were not saved. ", cut.Find("#page-error").TextContent.Trim()), Timeout);
        Assert.Equal("/debarr", cut.Find("#general-url-base").GetAttribute("value"));
        Assert.False(cut.Find("#general-save").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_save_from_another_tab_reloads_the_page_and_keeps_an_unsaved_edit()
    {
        var cut = RenderPage<GeneralPage>();
        cut.WaitForElement("#general-url-base", Timeout);
        await cut.RaiseInputAsync("#general-url-base", "/debarr", Timeout);
        cut.WaitForAssertion(() => Assert.False(cut.Find("#general-save").HasAttribute("disabled")), Timeout);

        await GetAppService<HostSettingsFile>().SaveAsync(
            new Dictionary<string, JsonNode> { ["Ffmpeg:FfmpegPath"] = JsonValue.Create("/opt/ffmpeg/ffmpeg") },
            CancellationToken);

        cut.WaitForElement("#general-restart-notice", Timeout);
        Assert.Equal("/debarr", cut.Find("#general-url-base").GetAttribute("value"));
        Assert.False(cut.Find("#general-save").HasAttribute("disabled"));
    }
}
