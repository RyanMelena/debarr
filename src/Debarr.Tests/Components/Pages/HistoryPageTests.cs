using Bunit;
using Debarr.Components.Layout;
using Debarr.Components.Pages;
using Debarr.Playing;
using Debarr.Tests.Extensions;
using Debarr.Tests.Playing;
using Microsoft.AspNetCore.Components;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Debarr.Tests.Components.Pages;

public sealed class HistoryPageTests : PageTestContext
{
    [Fact]
    public async Task History_pages_with_the_same_pager_as_media()
    {
        await SeedPlaybacksAsync(60);

        var cut = RenderPage<HistoryPage>();

        cut.WaitForAssertion(() => Assert.Contains("1-50 of 60", cut.Find("#history .mud-table-pagination").TextContent), Timeout);
        Assert.Equal(50, cut.FindAll("#history tbody tr").Count);
    }

    [Fact]
    public async Task A_link_with_an_unknown_sort_shows_the_newest_first_and_the_url_names_the_view_shown()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedPlaybacksAsync(
            TestPlayback.Handled(title: "Older") with { OccurredAt = now.AddMinutes(-1) },
            TestPlayback.Handled(title: "Newer") with { OccurredAt = now });

        var cut = RenderPage<HistoryPage>("?sort=bogus&dir=asc&size=25");

        cut.WaitForAssertion(() => Assert.Equal(["Newer", "Older"], Titles(cut)), Timeout);
        cut.WaitForAssertion(() => Assert.EndsWith("/?size=25", Uri), Timeout);
    }

    [Fact]
    public async Task Choosing_a_page_size_shows_its_first_page_and_puts_only_the_size_in_the_url()
    {
        await SeedPlaybacksAsync(120);
        var cut = RenderPage<HistoryPage>("?page=2");
        cut.WaitForAssertion(() => Assert.Contains("51-100 of 120", cut.Find("#history .mud-table-pagination").TextContent), Timeout);
        var navigations = CountNavigations();

        await cut.InvokeAsync(() => cut.FindComponent<MudTable<PlaybackRow>>().Instance.SetRowsPerPage(100));

        cut.WaitForAssertion(() => Assert.Contains("1-100 of 120", cut.Find("#history .mud-table-pagination").TextContent), Timeout);
        Assert.EndsWith("?size=100", Uri);
        Assert.Equal(1, navigations.Value);
    }

    [Fact]
    public async Task A_link_with_a_page_size_and_a_page_shows_that_page()
    {
        await SeedPlaybacksAsync(120);
        var cut = RenderPage<HistoryPage>();
        cut.WaitForAssertion(() => Assert.Contains("1-50 of 120", cut.Find("#history .mud-table-pagination").TextContent), Timeout);
        var navigations = CountNavigations();

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/history?size=100&page=2"));

        cut.WaitForAssertion(() => Assert.Contains("101-120 of 120", cut.Find("#history .mud-table-pagination").TextContent), Timeout);
        Assert.Equal(20, cut.FindAll("#history tbody tr").Count);
        Assert.EndsWith("/history?size=100&page=2", Uri);
        Assert.Equal(1, navigations.Value);
    }

    [Fact]
    public async Task Clear_history_asks_first_with_a_red_button_named_for_the_action_and_cancel_keeps_every_playback()
    {
        await SeedPlaybacksAsync(2);
        var cut = RenderPage<HistoryPage>();
        cut.WaitForAssertion(() => Assert.False(cut.Find("#history-clear").HasAttribute("disabled")), Timeout);

        await cut.RaiseClickAsync("#history-clear", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Clear all 2 playbacks", cut.Find("#confirm-message").TextContent), Timeout);
        var accept = cut.Find("#confirm-accept");
        Assert.Equal("Clear History", accept.TextContent.Trim());
        Assert.Contains("mud-button-filled-error", accept.ClassList);
        await cut.RaiseClickAsync("#confirm-cancel", Timeout);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#confirm-message")), Timeout);
        await using var session = GetAppService<IDocumentStore>().QuerySession();
        Assert.Equal(2, await session.CountShownPlaybackRowsAsync(CancellationToken));
    }

    [Fact]
    public async Task A_history_clear_empties_the_open_page()
    {
        await SeedPlaybacksAsync(2);
        var cut = RenderPage<HistoryPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, Titles(cut).Count), Timeout);

        await ClearHistoryAsync();

        cut.WaitForAssertion(() => Assert.Empty(Titles(cut)), Timeout);
    }

    [Fact]
    public async Task A_playback_that_sent_nothing_says_not_sent_and_why()
    {
        await SeedPlaybacksAsync(TestPlayback.Handled(playerPath: "plugin://plugin.video.youtube/play"));

        var cut = RenderPage<HistoryPage>();

        cut.WaitForAssertion(() => Assert.Equal("Not sent: a stream", cut.Find(".history-sent").TextContent.Trim()), Timeout);
        Assert.Contains("history-no-deliveries", cut.Find(".history-deliveries").ClassList);
    }

    [Fact]
    public async Task A_playback_lists_its_deliveries_in_the_order_they_finished()
    {
        var playback = Sent("Theater", "Heat");
        var sink = TestPlayback.Delivery(TestPlayback.LightsId, "Sink", new DeliveryOutcome.Succeeded());
        var automation = TestPlayback.Delivery(TestPlayback.AutomationId, "Automation", new DeliveryOutcome.Failed("Connection refused."));
        await TestPlayback.RecordAsync(GetAppService<IDocumentStore>(), playback, [sink, automation], CancellationToken);

        var cut = RenderPage<HistoryPage>();

        cut.WaitForAssertion(
            () => Assert.Equal(["Sink", "Automation"], cut.FindAll(".history-deliveries > div > div").Select(delivery => delivery.TextContent.Split('·')[0].Trim())),
            Timeout);
    }

    [Fact]
    public async Task The_outcome_chips_count_the_playbacks_the_player_and_search_match()
    {
        await SeedPlaybacksAsync(Sent("Theater", "Heat"), Sent("Bedroom", "Heat"), Sent("Theater", "Alien"), Clip());

        var cut = RenderPage<HistoryPage>("history?player=Theater&search=heat");

        cut.WaitForAssertion(() => Assert.Equal(["Heat"], Titles(cut)), Timeout);
        Assert.Equal("All 1", ChipText(cut, "all"));
        Assert.Equal("Sent 1", ChipText(cut, "sent"));
        Assert.Equal("Not Sent 0", ChipText(cut, "not-sent"));
    }

    [Fact]
    public async Task Selecting_an_outcome_shows_its_playbacks_and_puts_it_in_the_url()
    {
        await SeedPlaybacksAsync(Sent("Theater", "Heat"), Clip());
        var cut = RenderPage<HistoryPage>("history");
        cut.WaitForAssertion(() => Assert.Equal(2, Titles(cut).Count), Timeout);

        await cut.RaiseClickAsync("#history-filter-not-sent", Timeout);

        cut.WaitForAssertion(() => Assert.Equal(["Clip"], Titles(cut)), Timeout);
        Assert.Contains("outcome=not-sent", Uri);
    }

    [Fact]
    public async Task A_sent_ratio_links_to_the_detection_it_came_from()
    {
        var (videoFile, detection) = await SeedVideoFileAsync("/media/heat.mkv", 2.391);
        await SeedPlaybacksAsync(Sent("Theater", "Heat") with
        {
            VideoFile = videoFile,
            Outcome = new PlaybackOutcome.Sent(2.4, NotificationAspectRatioSource.Detected, detection.Id),
        });

        var cut = RenderPage<HistoryPage>("history");

        cut.WaitForAssertion(
            () => Assert.EndsWith($"video-file/{videoFile}#detection-{detection.Id}", cut.Find(".history-sent a").GetAttribute("href")),
            Timeout);
    }

    [Fact]
    public async Task A_playback_a_live_reload_brings_fades_in_and_a_change_of_view_fades_none()
    {
        await SeedPlaybacksAsync(Sent("Theater", "Alien") with { OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, Sent("Theater", "Heat"));
        var cut = RenderPage<HistoryPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, Titles(cut).Count), Timeout);
        Assert.Empty(cut.FindAll(".paged-table-row-enter"));

        var ran = Sent("Theater", "Ran") with { OccurredAt = DateTimeOffset.UtcNow.AddMinutes(1) };
        await SeedPlaybacksAsync(ran);

        cut.WaitForAssertion(() => Assert.Equal(["Ran"], cut.FindAll(".paged-table-row-enter td[data-label=Title]").Select(cell => cell.TextContent.Trim())), Timeout);

        Services.GetRequiredService<NavigationManager>().NavigateTo("history?sort=title");
        cut.WaitForAssertion(() => Assert.Equal(["Alien", "Heat", "Ran"], Titles(cut)), Timeout);
        Assert.Empty(cut.FindAll(".paged-table-row-enter"));
    }

    private static List<string> Titles(IRenderedComponent<UISettingsProvider> cut) =>
        cut.FindAll("#history tbody td[data-label=Title]").Select(cell => cell.TextContent.Trim()).ToList();

    private static string ChipText(IRenderedComponent<UISettingsProvider> cut, string filter) =>
        string.Join(' ', cut.Find($"#history-filter-{filter}").TextContent.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

    private static PlaybackHandled Sent(string playerName, string title) =>
        TestPlayback.Handled(playerName, title, $"/media/{title}.mkv", $"/media/{title}.mkv");

    private static PlaybackHandled Clip() => TestPlayback.Handled(title: "Clip", playerPath: "plugin://plugin.video.youtube/play");

    private string Uri => Services.GetRequiredService<NavigationManager>().Uri;

    private Task SeedPlaybacksAsync(int count) =>
        SeedPlaybacksAsync(Enumerable.Range(0, count).Select(index => TestPlayback.Handled(playerPath: $"/media/film-{index}.mkv")));
}
