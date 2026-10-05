using Bunit;
using Debarr.Components.Pages;
using Debarr.Hosting;
using Debarr.Tests.Extensions;

namespace Debarr.Tests.Components.Pages;

public sealed class LogsPageTests : PageTestContext
{
    [Fact]
    public async Task The_level_chips_count_the_entries_the_search_matches_and_filter_the_table()
    {
        // The date sorts this file before the app's own, so the page opens on it.
        var logFiles = GetAppService<LogFileDirectory>();
        Directory.CreateDirectory(logFiles.Path);
        await File.WriteAllLinesAsync(
            Path.Combine(logFiles.Path, "debarr-29991231.log"),
            [
                "2999-12-31 10:00:00.000 +00:00 [INF] Debarr.Scanning.StartLibraryScan: StartLibraryScan succeeded in 3 ms.",
                "2999-12-31 10:00:01.000 +00:00 [WRN] Debarr.Playing.PlayerConnectionService: Bedroom disconnected",
                "2999-12-31 10:00:02.000 +00:00 [ERR] Debarr.Scanning.LibraryScanner: Could not record the scan. The device is not ready.",
                "System.IO.IOException: The device is not ready.",
            ],
            CancellationToken);

        var cut = RenderPage<LogsPage>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("#log-entries tbody tr").Count), Timeout);
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#log-files"));
        Assert.Equal("Error", cut.Find("#log-entries tbody tr .log-level").TextContent.Trim());
        Assert.Contains("The device is not ready.", cut.Find(".log-detail pre").TextContent);
        Assert.Equal("Warnings 2", ChipText(cut, "warnings"));

        await cut.RaiseClickAsync("#log-filter-errors", Timeout);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#log-entries tbody tr")), Timeout);

        await cut.RaiseClickAsync("#log-filter-all", Timeout);
        await cut.RaiseInputAsync("#log-search", "bedroom", Timeout);
        cut.WaitForAssertion(() => Assert.Equal("All 1", ChipText(cut, "all")), Timeout);
        Assert.Contains("Bedroom disconnected", cut.Find("#log-entries tbody").TextContent);
    }

    [Fact]
    public async Task A_new_entry_shows_while_the_page_is_open_and_the_filter_stays()
    {
        var logFiles = GetAppService<LogFileDirectory>();
        Directory.CreateDirectory(logFiles.Path);
        var file = Path.Combine(logFiles.Path, "debarr-29991231.log");
        await File.WriteAllLinesAsync(
            file,
            [
                "2999-12-31 10:00:00.000 +00:00 [WRN] Debarr.Playing.PlayerConnectionService: Bedroom disconnected",
                "2999-12-31 10:00:00.000 +00:00 [WRN] Debarr.Playing.PlayerConnectionService: Bedroom disconnected",
                "2999-12-31 10:00:01.000 +00:00 [INF] Debarr.Scanning.StartLibraryScan: StartLibraryScan succeeded in 3 ms.",
            ],
            CancellationToken);

        var cut = RenderPage<LogsPage>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("#log-entries tbody tr").Count), Timeout);
        await cut.RaiseClickAsync("#log-filter-warnings", Timeout);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#log-entries tbody tr").Count), Timeout);

        await File.AppendAllLinesAsync(
            file,
            ["2999-12-31 10:00:02.000 +00:00 [ERR] Debarr.Scanning.LibraryScanner: Could not record the scan. The device is not ready."],
            CancellationToken);

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("#log-entries tbody tr").Count), Timeout);
        Assert.Equal("Could not record the scan. The device is not ready.", cut.Find("#log-entries tbody tr .log-message-text").TextContent.Trim());
        Assert.Equal("Warnings 3", ChipText(cut, "warnings"));
        Assert.Equal("All 4", ChipText(cut, "all"));
    }

    private static string ChipText(IRenderedComponent<Debarr.Components.Layout.UISettingsProvider> cut, string filter) =>
        string.Join(' ', cut.Find($"#log-filter-{filter}").TextContent.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
}
