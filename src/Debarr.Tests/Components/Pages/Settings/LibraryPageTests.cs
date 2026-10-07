using Debarr.Tests.Extensions;
using System.Reactive.Linq;
using Bunit;
using Debarr.Activity;
using Debarr.Components.Pages.Settings;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Scanning;
using Fisher;
using Microsoft.Data.Sqlite;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages.Settings;

public sealed class LibraryPageTests : PageTestContext
{
    [Fact]
    public async Task A_root_whose_scan_failed_shows_its_last_scan_as_failed()
    {
        var store = GetAppService<IDocumentStore>();
        await TestLibrary.AddRootFolderAsync(store, Path.Combine(Path.GetTempPath(), "debarr-missing"), CancellationToken, scannedAt: DateTimeOffset.UtcNow, scanError: "The folder is missing.");
        await TestLibrary.AddRootFolderAsync(store, Path.Combine(Path.GetTempPath(), "debarr-scanned"), CancellationToken, scannedAt: DateTimeOffset.UtcNow);

        var cut = RenderPage<LibraryPage>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".library-root-folder-last-scan").Count), Timeout);
        var lastScans = cut.FindAll(".library-root-folder-last-scan").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.StartsWith("Failed ", lastScans[0]);
        Assert.DoesNotContain("Failed", lastScans[1]);
    }

    [Fact]
    public async Task A_root_the_running_library_scan_has_still_to_finish_shows_scanning()
    {
        var store = GetAppService<IDocumentStore>();
        var scanned = Path.Combine(Path.GetTempPath(), "debarr-scanned");
        await TestLibrary.AddRootFolderAsync(store, scanned, CancellationToken);
        await TestLibrary.AddRootFolderAsync(store, Path.Combine(Path.GetTempPath(), "debarr-waiting"), CancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(store, Guid.CreateVersion7(), [new LibraryScanStarted(startedAt), new RootFolderScanned(new LocalPath(scanned), startedAt, null)], CancellationToken);

        var cut = RenderPage<LibraryPage>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".library-root-folder-last-scan").Count), Timeout);
        var lastScans = cut.FindAll(".library-root-folder-last-scan").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.NotEqual("Scanning", lastScans[0]);
        Assert.Equal("Scanning", lastScans[1]);
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_a_root_folder_its_scan_or_its_file_paths()
    {
        var store = GetAppService<IDocumentStore>();
        var root = Path.Combine(Path.GetTempPath(), "debarr-live");
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-root-folders-empty", Timeout);

        await TestLibrary.AddRootFolderAsync(store, root, CancellationToken, enabled: false);
        cut.WaitForAssertion(() => Assert.Equal(root, cut.Find(".library-root-folder-path").TextContent.Trim()), Timeout);

        var scannedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(
            store,
            Guid.CreateVersion7(),
            [new LibraryScanStarted(scannedAt), new RootFolderScanned(new LocalPath(root), scannedAt, "The folder is missing.")],
            CancellationToken);
        cut.WaitForAssertion(() => Assert.StartsWith("Failed ", cut.Find(".library-root-folder-last-scan").TextContent.Trim()), Timeout);

        await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.39);
        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find(".library-root-folder-file-paths").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task A_root_folder_removal_shows_among_the_running_work_while_it_runs()
    {
        var store = GetAppService<IDocumentStore>();
        var root = Path.Combine(Path.GetTempPath(), "debarr-removed");
        await TestLibrary.AddRootFolderAsync(store, root, CancellationToken);

        // A file with a result stays out of the detection queue, whose file scan would remove a path with no file behind it.
        await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.39);
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-root-folders", Timeout);
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#library-root-folders"));

        await WhileRootFolderRemovalRunsAsync(root, () =>
        {
            cut.WaitForAssertion(() => Assert.Equal($"Removing {root}", cut.Find("#running-work-root-folder-removal a").TextContent.Trim()), Timeout);
            Assert.EndsWith("settings/library", cut.Find("#running-work-root-folder-removal a").GetAttribute("href"));
        });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#running-work-root-folder-removal")), Timeout);
        Assert.Empty(await TestVideoFile.ReadMediaRowsAsync(store, CancellationToken));
    }

    [Fact]
    public async Task A_root_folder_the_library_dropped_shows_as_removing_with_its_file_paths_until_its_removal_ends()
    {
        var root = Path.Combine(Path.GetTempPath(), "debarr-removing");
        await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.39);
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-root-folders-empty", Timeout);

        await WhileRootFolderRemovalRunsAsync(root, () =>
        {
            cut.WaitForAssertion(() => Assert.StartsWith("Removing for ", cut.Find("#library-root-folder-removing .library-root-folder-last-scan").TextContent.Trim()), Timeout);
            var row = cut.Find("#library-root-folder-removing");
            Assert.Equal(root, row.QuerySelector(".library-root-folder-path")!.TextContent.Trim());
            Assert.Equal("1", row.QuerySelector(".library-root-folder-file-paths")!.TextContent.Trim());
            Assert.Empty(row.QuerySelectorAll("button, input"));
            Assert.Empty(cut.FindAll("#library-root-folders-empty"));
            StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#library-root-folders"));
        });

        cut.WaitForElement("#library-root-folders-empty", Timeout);
        Assert.Empty(cut.FindAll("#library-root-folder-removing"));
    }

    [Fact]
    public async Task A_confirmed_removal_shows_its_root_folder_as_removing_until_the_removal_ends()
    {
        var root = Path.Combine(Path.GetTempPath(), "debarr-confirmed");
        await TestLibrary.AddRootFolderAsync(GetAppService<IDocumentStore>(), root, CancellationToken);
        await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.39);
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-root-folders", Timeout);

        using var connection = new SqliteConnection(UnpooledConnectionString);
        SqliteTransaction? writeLockTheRemovalWaitsOn = null;
        using (GetAppService<ActivityFeed>().Events
            .OfType<CommittedEvent>()
            .Where(committed => committed.Event.Data is RootFolderRemoved)
            .Take(1)
            .Subscribe(_ =>
            {
                connection.Open();
                writeLockTheRemovalWaitsOn = connection.BeginTransaction();
            }))
        {
            await cut.RaiseClickAsync("button[aria-label^='Remove ']", Timeout);
            await cut.RaiseClickAsync("#confirm-accept", Timeout);

            cut.WaitForAssertion(() => Assert.StartsWith("Removing for ", cut.Find("#library-root-folder-removing .library-root-folder-last-scan").TextContent.Trim()), Timeout);
            Assert.Empty(cut.Find("#library-root-folder-removing").QuerySelectorAll("button, input"));
            Assert.Empty(cut.FindAll("#page-error"));
        }

        writeLockTheRemovalWaitsOn!.Dispose();
        cut.WaitForElement("#library-root-folders-empty", Timeout);
    }

    [Fact]
    public async Task A_refused_root_folder_shows_beneath_the_folder_field_and_clears_once_the_folder_changes()
    {
        var root = Directory.CreateTempSubdirectory("debarr-root-");
        try
        {
            var inside = root.CreateSubdirectory("Movies");
            await AddRootFolderAsync(root);
            var cut = RenderPage<LibraryPage>();
            cut.WaitForElement("#library-root-folders", Timeout);
            await cut.RaiseInputAsync("#library-root-folder-path", inside.FullName, Timeout);

            await cut.RaiseClickAsync("#library-add-root", Timeout);

            cut.WaitForAssertion(
                () => Assert.Contains($"{inside.FullName} is inside the root folder {root.FullName}.", FolderField(cut).TextContent),
                Timeout);
            Assert.Empty(cut.FindAll("#page-error"));
            Assert.Equal(inside.FullName, cut.Find("#library-root-folder-path").GetAttribute("value"));

            await cut.RaiseInputAsync("#library-root-folder-path", Path.Combine(Path.GetTempPath(), "debarr-other"), Timeout);

            cut.WaitForAssertion(() => Assert.DoesNotContain("is inside", FolderField(cut).TextContent), Timeout);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_folder_browser_opens_at_the_typed_folder_and_puts_the_folder_chosen_in_the_folder_field_without_adding_it()
    {
        var root = Directory.CreateTempSubdirectory("debarr-root-");
        try
        {
            var movies = root.CreateSubdirectory("Movies");
            File.WriteAllText(Path.Combine(root.FullName, "film.mkv"), "");
            var cut = RenderPage<LibraryPage>();
            cut.WaitForElement("#library-root-folders-empty", Timeout);
            await cut.RaiseInputAsync("#library-root-folder-path", root.FullName, Timeout);

            await cut.RaiseClickAsync("button[aria-label='Choose Folder']", Timeout);

            cut.WaitForAssertion(() => Assert.Equal(root.FullName, cut.Find("#folder-browser-path").GetAttribute("value")), Timeout);
            Assert.Equal(["..", "Movies"], cut.FindAll(".folder-browser-folder").Select(row => row.TextContent.Trim()));
            await cut.FindAll(".folder-browser-folder").Single(row => row.TextContent.Trim() == "Movies").ClickAsync();

            cut.WaitForAssertion(() => Assert.Equal(movies.FullName, cut.Find("#folder-browser-path").GetAttribute("value")), Timeout);
            Assert.Equal("This folder holds no folders.", cut.Find("#folder-browser-empty").TextContent.Trim());
            await cut.RaiseClickAsync("#folder-browser-choose", Timeout);

            cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#folder-browser-path")), Timeout);
            Assert.Equal(movies.FullName, cut.Find("#library-root-folder-path").GetAttribute("value"));
            Assert.Single(cut.FindAll("#library-root-folders-empty"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_folder_browser_shows_a_typed_folder_it_cannot_find_beneath_its_field_and_chooses_nothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "debarr-missing-folder");
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-root-folders-empty", Timeout);
        await cut.RaiseClickAsync("button[aria-label='Choose Folder']", Timeout);
        cut.WaitForElement("#folder-browser-path", Timeout);

        await cut.RaiseInputAsync("#folder-browser-path", missing, Timeout);

        cut.WaitForAssertion(
            () => Assert.Contains($"Debarr cannot find the folder {missing}.", cut.Find("#folder-browser-path").Closest(".mud-input-control")!.TextContent),
            Timeout);
        Assert.True(cut.Find("#folder-browser-choose").HasAttribute("disabled"));
        await cut.RaiseClickAsync("#folder-browser-cancel", Timeout);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#folder-browser-path")), Timeout);
        Assert.Equal("", cut.Find("#library-root-folder-path").GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task A_relative_folder_is_refused_beneath_the_folder_field()
    {
        var cut = RenderPage<LibraryPage>();
        cut.WaitForElement("#library-root-folders-empty", Timeout);
        await cut.RaiseInputAsync("#library-root-folder-path", "movies", Timeout);

        await cut.RaiseClickAsync("#library-add-root", Timeout);

        cut.WaitForAssertion(() => Assert.Contains("Enter a full path.", FolderField(cut).TextContent), Timeout);
        Assert.Empty(cut.FindAll("#page-error"));
    }

    [Fact]
    public async Task Save_reads_saving_and_holds_the_root_folder_actions_while_it_runs()
    {
        var root = Directory.CreateTempSubdirectory("debarr-root-");
        try
        {
            await AddRootFolderAsync(root);
            var cut = RenderPage<LibraryPage>();
            cut.WaitForElement("#library-root-folders", Timeout);
            await cut.RaiseInputAsync("#library-root-folder-path", Path.Combine(Path.GetTempPath(), "debarr-other"), Timeout);
            await cut.RaiseInputAsync("#library-scan-interval", "7", Timeout);

            // The save's write waits for this write lock, so the save runs until the lock is released.
            await using (var connection = new SqliteConnection(UnpooledConnectionString))
            {
                await connection.OpenAsync(CancellationToken);
                await using var transaction = connection.BeginTransaction();

                await cut.RaiseClickAsync("#library-save", Timeout);

                cut.WaitForAssertion(() => Assert.Equal("Saving", cut.Find("#library-save").TextContent.Trim()), Timeout);
                Assert.True(cut.Find("#library-add-root").HasAttribute("disabled"));
                Assert.True(cut.Find("button[aria-label^='Remove ']").HasAttribute("disabled"));
            }

            cut.WaitForAssertion(() => Assert.Equal("Save", cut.Find("#library-save").TextContent.Trim()), Timeout);
            Assert.True(cut.Find("#library-save").HasAttribute("disabled"));
            Assert.False(cut.Find("#library-add-root").HasAttribute("disabled"));
            Assert.Empty(cut.FindAll("#page-error"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_save_from_a_tab_whose_settings_another_tab_saved_under_its_edits_is_refused_and_both_saves_are_kept()
    {
        var first = RenderPage<LibraryPage>();
        var second = RenderPage<LibraryPage>(tab: OpenAnotherTab());
        first.WaitForElement("#library-root-folders-empty", Timeout);
        second.WaitForElement("#library-root-folders-empty", Timeout);
        await first.RaiseInputAsync("#library-video-extensions", "mkv", Timeout);
        var firstPage = first.FindComponent<LibraryPage>();
        var rendersBeforeSecondSave = firstPage.RenderCount;

        await second.RaiseInputAsync("#library-scan-interval", "7", Timeout);
        await second.RaiseClickAsync("#library-save", Timeout);
        await Poll.UntilAsync(async () => (await ReadLibrarySettingsAsync()).ScanInterval.Hours == 7, Timeout);
        firstPage.WaitForState(() => firstPage.RenderCount > rendersBeforeSecondSave, Timeout);

        await first.RaiseClickAsync("#library-save", Timeout);

        first.WaitForAssertion(() => Assert.Equal("Saved in another tab. Reload to see the change.", first.Find("#page-error").TextContent.Trim()), Timeout);
        Assert.Equal("mkv", first.Find("#library-video-extensions").GetAttribute("value"));
        var saved = await ReadLibrarySettingsAsync();
        Assert.Equal(7, saved.ScanInterval.Hours);
        Assert.Equal(VideoExtensions.Default, saved.VideoExtensions);
    }

    private async Task<LibrarySettings> ReadLibrarySettingsAsync()
    {
        await using var session = GetAppService<IDocumentStore>().QuerySession();
        return (await Library.ReadAsync(session, CancellationToken)).Settings;
    }

    private async Task AddRootFolderAsync(DirectoryInfo root) =>
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(new AddRootFolder(new LocalPath(root.FullName), DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);

    /// <summary>The Folder field's control, which holds its error text.</summary>
    private static AngleSharp.Dom.IElement FolderField(IRenderedComponent<Debarr.Components.Layout.UISettingsProvider> cut) =>
        cut.Find("#library-root-folder-path").Closest(".mud-input-control")!;
}
