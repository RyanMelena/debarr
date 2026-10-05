using Bunit;
using Debarr.Components.Pages.Settings;
using Debarr.Detecting;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Fisher;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class DetectionPageTests : PageTestContext
{
    private static readonly string[] AdvancedFields =
        ["#detection-sample-count", "#detection-skip-start-and-end", "#detection-timeout", "#detection-black-level-sdr", "#detection-black-level-hdr", "#detection-match-tolerance"];

    [Fact]
    public async Task Show_advanced_reveals_the_settings_an_operator_rarely_changes()
    {
        var cut = RenderPage<DetectionPage>();
        cut.WaitForElement("#detection-simultaneous-detections", Timeout);
        Assert.All(AdvancedFields, field => Assert.Empty(cut.FindAll(field)));

        await cut.RaiseClickAsync(".show-advanced", Timeout);

        cut.WaitForAssertion(() => Assert.All(AdvancedFields, field => Assert.Single(cut.FindAll(field))), Timeout);
        Assert.Equal("Hide Advanced", cut.Find(".show-advanced").TextContent.Trim());
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_the_settings_or_a_result()
    {
        var cut = RenderPage<DetectionPage>();
        cut.WaitForElement("#detection-simultaneous-detections", Timeout);

        await SendChangeDetectionSettingsAsync(settings => settings with { SimultaneousDetections = 3 });
        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find("#detection-simultaneous-detections").GetAttribute("value")), Timeout);

        await TestVideoFile.AddAsync(
            GetAppService<IDocumentStore>(),
            "/media/film.mkv",
            CancellationToken,
            followedBy: videoFile => [new AspectRatioDetected(videoFile, TestVideoFile.Detected(2.39, detectorVersion: AspectRatioDetector.Version - 1))]);
        cut.WaitForAssertion(() => Assert.Equal("1 detection result is from an older version.", cut.Find("#detection-older-count").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task A_refused_list_of_standard_ratios_shows_beside_the_ratio_field_and_keeps_the_edits()
    {
        var cut = RenderPage<DetectionPage>();
        await cut.RaiseInputAsync("#detection-simultaneous-detections", "3", Timeout);

        while (cut.FindAll("button[aria-label^='Remove ']") is not [])
        {
            await cut.RaiseClickAsync("button[aria-label^='Remove ']", Timeout);
        }

        await cut.RaiseClickAsync("#detection-save", Timeout);

        cut.WaitForAssertion(
            () => Assert.Contains("Add at least one standard ratio.", cut.Find("#detection-new-standard-ratio").Closest(".mud-input-control")!.TextContent),
            Timeout);
        Assert.Empty(cut.FindAll("#page-error"));
        Assert.Equal("3", cut.Find("#detection-simultaneous-detections").GetAttribute("value"));
        Assert.False(cut.Find("#detection-save").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_refused_simultaneous_detections_shows_beneath_its_field_and_clears_once_the_field_changes()
    {
        var cut = RenderPage<DetectionPage>();
        cut.WaitForElement("#detection-simultaneous-detections", Timeout);
        await cut.RaiseInputAsync("#detection-simultaneous-detections", "0", Timeout);
        cut.WaitForAssertion(() => Assert.Equal("0", cut.Find("#detection-simultaneous-detections").GetAttribute("value")), Timeout);

        await cut.RaiseClickAsync("#detection-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter 1 or more.", Control(cut, "#detection-simultaneous-detections").TextContent), Timeout);
        Assert.NotNull(Control(cut, "#detection-simultaneous-detections").QuerySelector(".mud-input-error"));

        await cut.RaiseInputAsync("#detection-simultaneous-detections", "2", Timeout);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Enter 1 or more.", Control(cut, "#detection-simultaneous-detections").TextContent), Timeout);
        Assert.Equal("2", cut.Find("#detection-simultaneous-detections").GetAttribute("value"));
    }

    [Fact]
    public async Task A_refused_setting_behind_show_advanced_turns_show_advanced_on_with_its_error()
    {
        var cut = RenderPage<DetectionPage>();
        cut.WaitForElement("#detection-simultaneous-detections", Timeout);
        await cut.RaiseClickAsync(".show-advanced", Timeout);
        cut.WaitForElement("#detection-sample-count", Timeout);
        await cut.RaiseInputAsync("#detection-sample-count", "0", Timeout);
        cut.WaitForAssertion(() => Assert.Equal("0", cut.Find("#detection-sample-count").GetAttribute("value")), Timeout);
        await cut.RaiseClickAsync(".show-advanced", Timeout);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#detection-sample-count")), Timeout);

        await cut.RaiseClickAsync("#detection-save", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter 1 or more.", Control(cut, "#detection-sample-count").TextContent), Timeout);
        Assert.Equal("Hide Advanced", cut.Find(".show-advanced").TextContent.Trim());
    }

    private static AngleSharp.Dom.IElement Control(IRenderedComponent<Debarr.Components.Layout.UISettingsProvider> cut, string field) =>
        cut.Find(field).Closest(".mud-input-control")!;
}
