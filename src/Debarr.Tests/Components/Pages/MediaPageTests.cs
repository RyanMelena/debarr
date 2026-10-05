using AngleSharp.Dom;
using Bunit;
using Debarr.Components.Layout;
using Debarr.Components.Pages;
using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Debarr.Tests.Extensions;
using Fisher;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Debarr.Tests.Components.Pages;

public sealed class MediaPageTests : PageTestContext
{
    private readonly DetectorDouble _detector;
    private readonly CommandRecorder _commands;

    public MediaPageTests()
        : this(new DetectorDouble(), new CommandRecorder())
    {
    }

    private MediaPageTests(DetectorDouble detector, CommandRecorder commands)
        : base(services =>
        {
            services.AddSingleton<IAspectRatioDetector>(detector);
            services.ConfigureFisher(options => options.Logger(commands));
        }) =>
        (_detector, _commands) = (detector, commands);

    [Fact]
    public async Task A_detection_updates_its_row_in_place_while_the_page_keeps_its_filter_search_and_sort()
    {
        await SeedFileAsync("alpha.mkv");
        var bravo = await SeedFileAsync("bravo.mkv");
        await SeedFileAsync("charlie.mkv");
        await SeedVideoFileAsync("/elsewhere/delta.mkv", 1.78);
        _detector.Holds = true;

        var cut = RenderPage<MediaPage>("?status=detected&search=media&sort=path&dir=desc");
        cut.WaitForAssertion(() => Assert.Equal(["charlie.mkv", "bravo.mkv", "alpha.mkv"], FileNames(cut)), Timeout);
        var rows = RowComponents(cut);

        Assert.True((await GetAppService<DetectionOrchestrator>().DetectNowAsync(bravo, CancellationToken)).IsSuccess);

        cut.WaitForAssertion(() => Assert.Matches(@"^Detecting\s+· \d+ s$", RowOf(cut, "bravo.mkv").QuerySelector(".media-detecting")?.TextContent.Trim() ?? ""), Timeout);
        Assert.Equal("Detect Now Busy", RowOf(cut, "alpha.mkv").QuerySelector(".media-detect-busy")?.TextContent.Trim());
        Assert.Contains(
            "Detect Now: Available when the Detect Now running on another file ends.",
            RowOf(cut, "alpha.mkv").QuerySelectorAll("button").Select(button => button.GetAttribute("aria-label")));
        cut.WaitForAssertion(() => Assert.Equal("Detecting bravo.mkv", cut.Find("#running-work-detections a").TextContent.Trim()), Timeout);

        _detector.ReleaseAll();

        cut.WaitForAssertion(() => Assert.Contains("100%", RowOf(cut, "bravo.mkv").TextContent), Timeout);
        Assert.Empty(cut.FindAll(".media-detecting"));
        Assert.Equal(["charlie.mkv", "bravo.mkv", "alpha.mkv"], FileNames(cut));
        Assert.EndsWith("?status=detected&search=media&sort=path&dir=desc", Uri);
        Assert.All(RowComponents(cut), row => Assert.Same(rows[row.Key], row.Value));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".running-work-item, #running-work-summary")), Timeout);
    }
    [Fact]
    public async Task The_ratio_column_shows_what_a_playback_sends_and_sorts_by_it()
    {
        await SeedVideoFileAsync("/media/a-detected.mkv", 2.391);
        await SeedVideoFileAsync("/media/b-overridden.mkv", 2.391, overrideAspectRatio: 2.0);
        await SeedVideoFileAsync("/media/c-suppressed.mkv", 1.78, dontSend: true);

        var cut = RenderPage<MediaPage>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".media-file-name").Count), Timeout);

        Assert.DoesNotContain("Source", cut.FindAll("#media th").Select(header => header.TextContent.Trim()));
        Assert.Single(cut.FindAll(".media-override"));
        Assert.Single(cut.FindAll(".media-not-sent"));
        Assert.Equal("2.391 raw", cut.Find(".media-raw").TextContent.Trim());

        await cut.FindAll(".mud-table-sort-label").Single(label => label.TextContent.Trim() == "Ratio").ClickAsync();

        cut.WaitForAssertion(
            () => Assert.Equal(
                ["c-suppressed.mkv", "b-overridden.mkv", "a-detected.mkv"],
                cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim())),
            Timeout);
        Assert.Contains("sort=ratio", Uri);
    }

    [Fact]
    public async Task A_phone_card_leaves_out_the_ratio_and_confidence_a_file_has_no_result_for()
    {
        await SeedVideoFileAsync("/media/a-detected.mkv", 2.391);
        await TestVideoFile.AddAsync(GetAppService<IDocumentStore>(), "/media/b-pending.mkv", CancellationToken);

        var cut = RenderPage<MediaPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".media-file-name").Count), Timeout);

        var rows = cut.FindAll("#media tbody tr").Where(row => row.QuerySelector(".media-file-name") is not null).ToList();
        Assert.Empty(rows[0].QuerySelectorAll("td.media-empty"));
        Assert.Equal(
            ["Ratio", "Confidence"],
            rows[1].QuerySelectorAll("td.media-empty").Select(cell => cell.GetAttribute("data-label")));
    }

    [Fact]
    public async Task A_link_with_an_unknown_sort_shows_the_default_sort_and_direction_and_the_url_names_the_view_shown()
    {
        await SeedVideoFileAsync("/media/a-detected.mkv", 2.391);
        await SeedVideoFileAsync("/media/b-manual.mkv", 2.391, overrideAspectRatio: 2.0);
        await SeedVideoFileAsync("/media/c-manual.mkv", 1.78, overrideAspectRatio: 1.85);

        var cut = RenderPage<MediaPage>("?status=manual&sort=added&dir=desc");

        cut.WaitForAssertion(
            () => Assert.Equal(["b-manual.mkv", "c-manual.mkv"], cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim())),
            Timeout);
        cut.WaitForAssertion(() => Assert.EndsWith("/?status=manual", Uri), Timeout);
    }

    [Fact]
    public async Task The_url_names_a_status_and_the_first_seen_sort_in_the_words_the_page_shows()
    {
        await SeedVideoFileAsync("/media/a-detected.mkv", 2.391);
        await SeedVideoFileAsync("/media/b-manual.mkv", 2.391, overrideAspectRatio: 2.0);
        await SeedVideoFileAsync("/media/c-manual.mkv", 1.78, overrideAspectRatio: 1.85);

        var cut = RenderPage<MediaPage>("?status=manual&sort=first-seen&dir=desc");

        cut.WaitForAssertion(
            () => Assert.Equal(["c-manual.mkv", "b-manual.mkv"], cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim())),
            Timeout);
        Assert.Contains("First Seen", cut.FindAll("#media th").Select(header => header.TextContent.Trim()));
    }

    [Fact]
    public async Task A_new_standard_ratio_changes_the_ratio_an_open_row_shows()
    {
        await SeedVideoFileAsync("/media/film.mkv", 2.12);
        var cut = RenderPage<MediaPage>();
        cut.WaitForAssertion(() => Assert.Equal("2.12", cut.Find(".media-raw").PreviousElementSibling?.TextContent.Trim()), Timeout);

        await SendChangeDetectionSettingsAsync(settings => settings with { StandardRatios = [.. settings.StandardRatios, new StandardRatio(2.10, false)] });

        cut.WaitForAssertion(() => Assert.Equal("2.10", cut.Find(".media-raw").PreviousElementSibling?.TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task The_url_sets_the_search_sort_and_direction_the_page_opens_with()
    {
        await SeedVideoFileAsync("/media/films/a.mkv", 1.78);
        await SeedVideoFileAsync("/media/films/b.mkv", 2.391);
        await SeedVideoFileAsync("/media/shows/c.mkv", 1.33);

        var cut = RenderPage<MediaPage>("?search=FILMS&sort=ratio&dir=desc");

        cut.WaitForAssertion(
            () => Assert.Equal(["b.mkv", "a.mkv"], cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim())),
            Timeout);
        Assert.Equal("FILMS", cut.Find("#media-search").GetAttribute("value"));
        Assert.Equal("Widest First", cut.Find(".sort-select-direction").TextContent.Trim());
    }

    [Fact]
    public async Task Reversing_the_phone_sort_puts_the_direction_in_the_url_and_reverses_the_rows()
    {
        await SeedVideoFileAsync("/media/a.mkv", 1.78);
        await SeedVideoFileAsync("/media/b.mkv", 2.391);

        var cut = RenderPage<MediaPage>();
        cut.WaitForAssertion(() => Assert.Equal("A to Z", cut.Find(".sort-select-direction").TextContent.Trim()), Timeout);

        await cut.RaiseClickAsync(".sort-select-direction", Timeout);

        cut.WaitForAssertion(
            () => Assert.Equal(["b.mkv", "a.mkv"], cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim())),
            Timeout);
        Assert.Equal("Z to A", cut.Find(".sort-select-direction").TextContent.Trim());
        Assert.Contains("dir=desc", Uri);
    }

    [Fact]
    public async Task Typing_a_search_filters_the_rows_and_puts_the_search_in_the_url()
    {
        await SeedVideoFileAsync("/media/films/heat.mkv", 2.391);
        await SeedVideoFileAsync("/media/shows/lost.mkv", 1.78);

        var cut = RenderPage<MediaPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".media-file-name").Count), Timeout);

        await cut.RaiseInputAsync("#media-search", "Heat", Timeout);

        cut.WaitForAssertion(() => Assert.Equal(["heat.mkv"], cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim())), Timeout);
        Assert.Contains("search=Heat", Uri);
    }

    [Fact]
    public async Task Choosing_a_page_size_shows_its_first_page_and_puts_only_the_size_in_the_url()
    {
        // The seeded files are pending and missing, and the queue's detections of them would reload the page throughout.
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        await SeedFilmsAsync(120);

        var cut = RenderPage<MediaPage>("?page=2");
        cut.WaitForAssertion(() => Assert.Contains("51-100 of 120", cut.Find("#media .mud-table-pagination").TextContent), Timeout);
        var navigations = CountNavigations();

        await cut.InvokeAsync(() => cut.FindComponent<MudTable<MediaRow>>().Instance.SetRowsPerPage(100));

        cut.WaitForAssertion(() => Assert.Contains("1-100 of 120", cut.Find("#media .mud-table-pagination").TextContent), Timeout);
        Assert.Equal("film-000.mkv", cut.FindAll(".media-file-name")[0].TextContent.Trim());
        Assert.EndsWith("?size=100", Uri);
        Assert.Equal(1, navigations.Value);
    }

    [Fact]
    public async Task A_link_that_changes_the_page_size_and_the_page_reads_the_table_once()
    {
        // The seeded files are pending and missing, and a queue that dropped their paths would reload the page throughout.
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        await SeedFilmsAsync(250);
        var cut = RenderPage<MediaPage>("?page=2");
        cut.WaitForAssertion(() => Assert.Contains("51-100 of 250", cut.Find("#media .mud-table-pagination").TextContent), Timeout);

        // The seeded files' events reload the page, so the count starts once they have.
        await WaitForQuietAsync();
        await cut.InvokeAsync(() =>
        {
            _commands.Clear();
            Services.GetRequiredService<NavigationManager>().NavigateTo("?size=100&page=3");
        });

        cut.WaitForAssertion(() => Assert.Contains("201-250 of 250", cut.Find("#media .mud-table-pagination").TextContent), Timeout);
        Assert.Equal("film-200.mkv", cut.FindAll(".media-file-name")[0].TextContent.Trim());
        await WaitForQuietAsync();
        Assert.EndsWith(" limit 100 offset 200", Assert.Single(PageReads));
    }

    [Fact]
    public async Task A_file_a_live_reload_brings_fades_in_and_a_change_of_view_fades_none()
    {
        await SeedFileAsync("alpha.mkv");
        var bravo = await SeedFileAsync("bravo.mkv");
        var cut = RenderPage<MediaPage>();
        cut.WaitForAssertion(() => Assert.Equal(["alpha.mkv", "bravo.mkv"], FileNames(cut)), Timeout);

        await SeedFileAsync("charlie.mkv");
        Assert.True((await GetAppService<DetectionOrchestrator>().DetectNowAsync(bravo, CancellationToken)).IsSuccess);

        cut.WaitForAssertion(() => Assert.Equal(["alpha.mkv", "bravo.mkv", "charlie.mkv"], FileNames(cut)), Timeout);
        Assert.Equal(["charlie.mkv"], cut.FindAll(".paged-table-row-enter .media-file-name").Select(name => name.TextContent.Trim()));

        Services.GetRequiredService<NavigationManager>().NavigateTo("?dir=desc");
        cut.WaitForAssertion(() => Assert.Equal(["charlie.mkv", "bravo.mkv", "alpha.mkv"], FileNames(cut)), Timeout);
        Assert.Empty(cut.FindAll(".paged-table-row-enter"));
    }

    [Fact]
    public async Task A_video_file_with_two_paths_is_one_row_that_lists_both_and_a_search_on_either_finds_it()
    {
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        var store = GetAppService<IDocumentStore>();
        await TestVideoFile.AddAsync(store, ["/media/films/Heat.mkv", "/backup/heat-copy.mkv"], CancellationToken);
        await TestVideoFile.AddAsync(store, "/media/films/Lost.mkv", CancellationToken);

        var cut = RenderPage<MediaPage>();

        cut.WaitForAssertion(() => Assert.Equal(["heat-copy.mkv", "Lost.mkv"], FileNames(cut)), Timeout);
        Assert.Equal(["/media/films/Heat.mkv"], RowOf(cut, "heat-copy.mkv").QuerySelectorAll(".media-other-path").Select(path => path.TextContent.Trim()));
        Assert.Equal("2", cut.Find("#media-filter-all .filter-bar-count").TextContent.Trim());
        Assert.Equal("2", cut.Find("#media-filter-pending .filter-bar-count").TextContent.Trim());

        await cut.RaiseInputAsync("#media-search", "films/heat", Timeout);

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal(["heat-copy.mkv"], FileNames(cut));
                Assert.Equal("1", cut.Find("#media-filter-all .filter-bar-count").TextContent.Trim());
            },
            Timeout);
    }

    private string Uri => Services.GetRequiredService<NavigationManager>().Uri;

    /// <summary>The commands that read one page of the table.</summary>
    private IEnumerable<string> PageReads => _commands.Commands.Where(command => command.Contains(" offset ", StringComparison.Ordinal));

    /// <summary>Waits until the table has read no page for a second.</summary>
    private async Task WaitForQuietAsync()
    {
        var count = -1;
        while (PageReads.Count() != count)
        {
            count = PageReads.Count();
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        }
    }

    /// <summary>Adds pending video files at /media/film-000.mkv onwards, in one transaction.</summary>
    private async Task SeedFilmsAsync(int count)
    {
        var now = DateTimeOffset.UtcNow;
        await using var session = GetAppService<IDocumentStore>().LightweightSession();
        foreach (var index in Enumerable.Range(0, count))
        {
            var videoFile = TestFileHash.For($"{index}");
            await session.Events.AppendAtCurrentVersionAsync(
                videoFile.StreamId,
                new VideoFileDiscovered(videoFile, 1, now),
                new FilePathAdded(videoFile, new LocalPath($"/media/film-{index:D3}.mkv"), TestVideoFile.Stat, now, now));
        }

        await session.SaveChangesAsync(CancellationToken);
    }

    private static IEnumerable<string> FileNames(IRenderedComponent<UISettingsProvider> cut) =>
        cut.FindAll(".media-file-name").Select(name => name.TextContent.Trim());

    private static IElement RowOf(IRenderedComponent<UISettingsProvider> cut, string fileName) =>
        cut.FindAll("#media tbody tr").Single(row => row.QuerySelector(".media-file-name")?.TextContent.Trim() == fileName);

    /// <summary>Each row's component by its video file, which the table keeps while its key matches.</summary>
    private static Dictionary<FileHash, MudTr> RowComponents(IRenderedComponent<UISettingsProvider> cut) =>
        cut.FindComponents<MudTr>().ToDictionary(row => ((MediaRow)row.Instance.Item!).FileHash, row => row.Instance);
}
