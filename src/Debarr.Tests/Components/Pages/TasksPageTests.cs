using Bunit;
using Debarr.Components.Pages;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Debarr.Tests.Extensions;
using Debarr.Tests.Scanning;
using Fisher;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages;

public sealed class TasksPageTests : PageTestContext
{
    private readonly DetectorDouble _detector;
    private readonly Interleaver _interleaver;

    public TasksPageTests()
        : this(new DetectorDouble(), new Interleaver())
    {
    }

    private TasksPageTests(DetectorDouble detector, Interleaver interleaver)
        : base(services => services.AddSingleton<IAspectRatioDetector>(detector).ConfigureFisher(options => options.Listeners.Add(interleaver)))
    {
        _detector = detector;
        _interleaver = interleaver;
    }

    [Fact]
    public void The_navigation_lists_history_at_the_top_level_and_tasks_under_system()
    {
        var cut = RenderPage<TasksPage>();

        Assert.Equal(
            ["Media", "History", "Settings", "Library", "Detection", "Players", "Notifiers", "General", "UI", "System", "Status", "Tasks", "Logs"],
            cut.FindAll(".mud-nav-link").Select(link => (link.QuerySelector("#health-badge") is { } badge ? link.TextContent.Replace(badge.TextContent, "") : link.TextContent).Trim()));
    }

    [Fact]
    public void The_library_scan_shows_its_interval_and_that_no_root_is_enabled()
    {
        var cut = RenderPage<TasksPage>();

        cut.WaitForAssertion(() => Assert.Equal("12 hours", cut.Find(".tasks-library-scan-interval").TextContent.Trim()), Timeout);
        Assert.NotNull(cut.Find("#tasks-no-roots"));
    }

    [Fact]
    public void The_scheduled_description_says_the_startup_scan_runs_only_for_a_library_with_an_enabled_root_folder_or_a_video_file()
    {
        var cut = RenderPage<TasksPage>();

        cut.WaitForAssertion(
            () => Assert.Contains(
                "at startup when the library has an enabled root folder or a video file,",
                cut.FindAll(".page-section-description").Select(description => description.TextContent).First(text => text.Contains("A library scan reads"))),
            Timeout);
    }

    [Fact]
    public async Task The_library_scan_shows_its_next_run_until_the_schedule_is_switched_off()
    {
        var cut = RenderPage<TasksPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.Find(".tasks-library-scan-next-run").TextContent.Trim()), Timeout);

        var saved = await GetAppService<IWolverineRuntime>().SendCommandAsync(
            new ChangeLibrarySettings(VideoExtensions.Default.ToString(), null, true),
            CancellationToken);

        Assert.True(saved.IsSuccess);
        cut.WaitForAssertion(() => Assert.Equal("Off", cut.Find(".tasks-library-scan-interval").TextContent.Trim()), Timeout);
        cut.WaitForAssertion(() => Assert.Empty(cut.Find(".tasks-library-scan-next-run").TextContent.Trim()), Timeout);
    }

    [Theory]
    [InlineData("failed", "Failed ", "The folder is missing.")]
    [InlineData("cancelled", "Cancelled ", "Cancelled: Debarr shut down or a scan pause stopped the scan.")]
    public async Task A_failed_or_cancelled_library_scan_shows_its_outcome_in_its_last_run_and_beneath_its_counts(string outcome, string lastRun, string text)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(
            GetAppService<IDocumentStore>(),
            Guid.CreateVersion7(),
            [
                new LibraryScanStarted(startedAt),
                new LibraryScanEnded(
                    startedAt,
                    new LibraryScanCounts(2, 0, 0, 0, 0),
                    outcome == "failed" ? new LibraryScanOutcome.Failed("The folder is missing.") : new LibraryScanOutcome.Cancelled()),
            ],
            CancellationToken);

        var cut = RenderPage<TasksPage>();

        cut.WaitForAssertion(() => Assert.StartsWith(lastRun, cut.Find(".tasks-library-scan-last-run").TextContent.Trim()), Timeout);
        Assert.Equal(text, cut.Find(".tasks-library-scan-outcome").TextContent.Trim());
        Assert.Equal("2", cut.Find(".tasks-library-scan-found").TextContent.Trim());
    }

    [Fact]
    public async Task An_interrupted_library_scan_shows_as_interrupted_with_no_duration_or_counts()
    {
        await TestLibrary.AppendLibraryScanAsync(
            GetAppService<IDocumentStore>(),
            Guid.CreateVersion7(),
            [new LibraryScanStarted(DateTimeOffset.UtcNow), new LibraryScanInterrupted(DateTimeOffset.UtcNow)],
            CancellationToken);

        var cut = RenderPage<TasksPage>();

        cut.WaitForAssertion(() => Assert.StartsWith("Interrupted ", cut.Find(".tasks-library-scan-last-run").TextContent.Trim()), Timeout);
        Assert.Equal("Interrupted: Debarr stopped before the scan ended.", cut.Find(".tasks-library-scan-outcome").TextContent.Trim());
        Assert.Equal(["", ""], cut.FindAll("#tasks-library-scans tr").Skip(1).First().QuerySelectorAll(".tasks-library-scan-duration, .tasks-library-scan-found").Select(cell => cell.TextContent.Trim()));
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_adds_a_root_folder_or_a_library_scan()
    {
        var store = GetAppService<IDocumentStore>();
        var cut = RenderPage<TasksPage>();
        cut.WaitForElement("#tasks-no-roots", Timeout);
        var scans = cut.FindAll("#tasks-library-scans .tasks-library-scan-duration").Count;

        await TestLibrary.AddRootFolderAsync(store, Path.Combine(Path.GetTempPath(), "debarr-root"), CancellationToken);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#tasks-no-roots")), Timeout);

        var startedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(
            store,
            Guid.CreateVersion7(),
            [new LibraryScanStarted(startedAt), new LibraryScanEnded(startedAt, new LibraryScanCounts(0, 0, 0, 0, 0), new LibraryScanOutcome.Finished())],
            CancellationToken);
        cut.WaitForAssertion(() => Assert.Equal(scans + 1, cut.FindAll("#tasks-library-scans .tasks-library-scan-duration").Count), Timeout);
    }

    [Fact]
    public async Task Scan_now_starts_a_library_scan_that_the_page_then_lists()
    {
        var cut = RenderPage<TasksPage>();
        cut.WaitForElement("#tasks-library-scans-none", Timeout);

        await cut.RaiseClickAsync("#tasks-library-scan-run", Timeout);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#tasks-library-scans .tasks-library-scan-duration")), Timeout);
    }

    [Fact]
    public async Task Scan_now_reads_scanning_and_stays_disabled_while_its_scan_runs()
    {
        var cut = RenderPage<TasksPage>();
        cut.WaitForElement("#tasks-library-scans-none", Timeout);

        // The scan's last write waits here, so the scan runs until it is released.
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _interleaver.Before<LibraryScanEnded>(async () =>
        {
            await released.Task;
            return Result.Ok();
        });
        try
        {
            await cut.RaiseClickAsync("#tasks-library-scan-run", Timeout);

            cut.WaitForAssertion(() => Assert.Equal("Scanning", cut.Find("#tasks-library-scan-run").TextContent.Trim()), Timeout);
            Assert.True(cut.Find("#tasks-library-scan-run").HasAttribute("disabled"));
            Assert.Equal("Never", cut.Find(".tasks-library-scan-last-run").TextContent.Trim());

            // The navigation's running work reloads on its own.
            cut.WaitForAssertion(() => Assert.Equal("Scanning the library", cut.Find("#running-work-library-scan a").TextContent.Trim()), Timeout);
        }
        finally
        {
            released.SetResult();
        }

        cut.WaitForAssertion(() => Assert.Equal("Scan Now", cut.Find("#tasks-library-scan-run").TextContent.Trim()), Timeout);
        Assert.DoesNotContain("Running", Assert.Single(cut.FindAll("#tasks-library-scans .tasks-library-scan-duration")).TextContent);
        Assert.NotEqual("Never", cut.Find(".tasks-library-scan-last-run").TextContent.Trim());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#running-work-library-scan")), Timeout);
    }

    [Fact]
    public async Task A_library_scan_a_scan_pause_is_stopping_reads_stopping_in_the_running_work_and_its_row()
    {
        var cut = RenderPage<TasksPage>();
        cut.WaitForElement("#tasks-library-scans-none", Timeout);

        // The scan's last write waits here, so the scan is still running when the pause stops it.
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _interleaver.Before<LibraryScanEnded>(async () =>
        {
            await released.Task;
            return Result.Ok();
        });
        Task<ScanPause>? pause = null;
        try
        {
            await cut.RaiseClickAsync("#tasks-library-scan-run", Timeout);
            cut.WaitForAssertion(() => Assert.Equal("Scanning the library", cut.Find("#running-work-library-scan a").TextContent.Trim()), Timeout);

            pause = GetAppService<LibraryScanner>().PauseScansAsync(CancellationToken);

            cut.WaitForAssertion(() => Assert.Equal("Stopping the library scan", cut.Find("#running-work-library-scan a").TextContent.Trim()), Timeout);
            cut.WaitForAssertion(() => Assert.Equal("Stopping", cut.Find("#tasks-library-scans .tasks-library-scan-duration").TextContent.Trim()), Timeout);
            Assert.False(pause.IsCompleted, "the pause should wait for the scan to end");
        }
        finally
        {
            released.SetResult();
            if (pause is not null)
            {
                (await pause).Dispose();
            }
        }

        cut.WaitForAssertion(() => Assert.Equal("Scan Now", cut.Find("#tasks-library-scan-run").TextContent.Trim()), Timeout);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#running-work-library-scan")), Timeout);
    }

    [Fact]
    public async Task The_running_library_scan_leads_the_library_scans_as_running_with_no_counts()
    {
        var store = GetAppService<IDocumentStore>();
        var startedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(
            store,
            Guid.CreateVersion7(),
            [new LibraryScanStarted(startedAt.AddHours(-1)), new LibraryScanEnded(startedAt.AddHours(-1), new LibraryScanCounts(3, 0, 0, 0, 0), new LibraryScanOutcome.Finished())],
            CancellationToken);
        await TestLibrary.AppendLibraryScanAsync(store, Guid.CreateVersion7(), [new LibraryScanStarted(startedAt)], CancellationToken);

        var cut = RenderPage<TasksPage>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#tasks-library-scans .tasks-library-scan-duration").Count), Timeout);
        var durations = cut.FindAll("#tasks-library-scans .tasks-library-scan-duration").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.StartsWith("Running for", durations[0]);
        Assert.DoesNotContain("Running", durations[1]);
        Assert.Equal(["", "3"], cut.FindAll("#tasks-library-scans .tasks-library-scan-found").Select(cell => cell.TextContent.Trim()));
        Assert.NotEqual("Never", cut.Find(".tasks-library-scan-last-run").TextContent.Trim());
        Assert.Empty(cut.FindAll(".tasks-library-scan-outcome"));
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#tasks-scheduled"));
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#tasks-library-scans"));
    }

    [Fact]
    public async Task A_running_detection_shows_under_detections_until_it_ends()
    {
        var videoFile = await SeedFileAsync("film.mkv");
        _detector.Holds = true;
        var cut = RenderPage<TasksPage>();
        cut.WaitForElement("#tasks-detections-none", Timeout);

        Assert.True((await GetAppService<DetectionOrchestrator>().DetectNowAsync(videoFile, CancellationToken)).IsSuccess);

        cut.WaitForAssertion(() => Assert.EndsWith("film.mkv", cut.Find(".tasks-detection a").TextContent.Trim()), Timeout);
        Assert.Equal($"video-file/{videoFile}", cut.Find(".tasks-detection a").GetAttribute("href"));
        _detector.ReleaseAll();
        cut.WaitForElement("#tasks-detections-none", Timeout);
    }

    [Fact]
    public async Task A_running_root_folder_removal_shows_under_root_folder_removal_until_it_ends()
    {
        var root = Path.Combine(Path.GetTempPath(), "debarr-removing");
        await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.39);
        var cut = RenderPage<TasksPage>();
        cut.WaitForElement("#tasks-root-folder-removal-none", Timeout);

        await WhileRootFolderRemovalRunsAsync(root, () =>
        {
            cut.WaitForAssertion(() => Assert.Equal(root, cut.Find("#tasks-root-folder-removal a").TextContent.Trim()), Timeout);
            Assert.EndsWith("settings/library", cut.Find("#tasks-root-folder-removal a").GetAttribute("href"));
            Assert.NotEmpty(cut.Find("#tasks-root-folder-removal .tasks-root-folder-removal-elapsed").TextContent.Trim());
            StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#tasks-root-folder-removals"));
        });

        cut.WaitForElement("#tasks-root-folder-removal-none", Timeout);
        Assert.Empty(cut.FindAll("#tasks-root-folder-removal"));
    }
}
