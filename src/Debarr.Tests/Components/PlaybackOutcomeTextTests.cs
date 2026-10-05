using Bunit;
using Debarr.Components;
using Debarr.Detecting;
using Debarr.Playing;
using MudBlazor.Services;

namespace Debarr.Tests.Components;

public sealed class PlaybackOutcomeTextTests : BunitContext
{
    public PlaybackOutcomeTextTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(NotificationAspectRatioSource.Detected, VideoFileStatus.Detected)]
    [InlineData(NotificationAspectRatioSource.Manual, VideoFileStatus.Manual)]
    [InlineData(NotificationAspectRatioSource.Container, VideoFileStatus.FromFile)]
    public void A_sent_ratio_has_the_colour_and_icon_of_the_status_it_came_from(NotificationAspectRatioSource source, VideoFileStatus status)
    {
        var outcome = Render<PlaybackOutcomeText>(parameters => parameters.Add(text => text.Outcome, new PlaybackOutcome.Sent(2.4, source, null))).Find(".state-label");
        var statusLabel = Render<VideoFileStatusText>(parameters => parameters.Add(text => text.Status, status)).Find(".state-label");

        Assert.Equal(statusLabel.ClassName!.Replace("video-file-status", "").Trim(), outcome.ClassName!.Replace("playback-outcome", "").Trim());
        Assert.Equal(statusLabel.QuerySelector("svg")!.InnerHtml, outcome.QuerySelector("svg")!.InnerHtml);
    }

    [Theory]
    [InlineData(NotificationAspectRatioSource.Detected, "2.40 detected")]
    [InlineData(NotificationAspectRatioSource.Container, "2.40 from file")]
    [InlineData(NotificationAspectRatioSource.Manual, "2.40 manual")]
    [InlineData(NotificationAspectRatioSource.Player, "2.40 from the player")]
    public void A_sent_ratio_names_its_source_after_the_ratio(NotificationAspectRatioSource source, string text)
    {
        var outcome = Render<PlaybackOutcomeText>(parameters => parameters.Add(text => text.Outcome, new PlaybackOutcome.Sent(2.4, source, null))).Find(".state-label");

        Assert.Equal(text, outcome.TextContent.Trim());
    }

    public static TheoryData<PlaybackOutcome, string> NotSentOutcomes => new()
    {
        { new PlaybackOutcome.Stream(), "Not sent: a stream" },
        { new PlaybackOutcome.DontSend(), "Not sent: the override says don't send" },
        { new PlaybackOutcome.NoPlayerAspectRatio(), "Not sent: the player reported no ratio" },
        { new PlaybackOutcome.Failed("boom"), "Not sent: handling failed" },
    };

    [Theory]
    [MemberData(nameof(NotSentOutcomes))]
    public void A_playback_that_sent_nothing_says_why_in_secondary_text(PlaybackOutcome notSent, string text)
    {
        var outcome = Render<PlaybackOutcomeText>(parameters => parameters.Add(text => text.Outcome, notSent)).Find(".playback-outcome");

        Assert.Contains("mud-text-secondary", outcome.ClassList);
        Assert.Equal(text, outcome.TextContent.Trim());
    }

    [Fact]
    public void A_failed_playback_shows_its_error()
    {
        var labels = Render<PlaybackOutcomeText>(parameters => parameters.Add(text => text.Outcome, new PlaybackOutcome.Failed("boom"))).FindAll(".state-label");

        Assert.Equal(["Not sent: handling failed", "boom"], labels.Select(label => label.TextContent.Trim()));
    }
}
