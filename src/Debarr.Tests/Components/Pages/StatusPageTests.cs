using Bunit;
using Debarr.Components.Pages;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Notifying;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Debarr.Tests.Playing;
using Debarr.Tests.Scanning;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages;

public sealed class StatusPageTests : PageTestContext
{
    // A detection the orchestrator starts holds, so a seeded file keeps the status it was seeded with.
    public StatusPageTests()
        : base(services => services.AddSingleton<IAspectRatioDetector>(new DetectorDouble { Holds = true }))
    {
    }

    [Fact]
    public void Health_leads_the_page_and_about_ends_it()
    {
        var cut = RenderPage<StatusPage>();

        cut.WaitForAssertion(
            () => Assert.Equal(["Health", "Library", "Detection", "Players", "Notifiers", "About"], cut.FindAll(".status h6").Select(heading => heading.TextContent.Trim())),
            Timeout);
    }

    [Fact]
    public void About_shows_the_version_with_a_short_commit_hash_and_when_the_app_started()
    {
        var cut = RenderPage<StatusPage>();

        cut.WaitForElement("#status-started", Timeout);
        Assert.DoesNotMatch(@"\+[0-9a-f]{8,}", cut.Find("#status-version").TextContent.Trim());
        Assert.NotEmpty(cut.Find("#status-started").TextContent.Trim());
    }

    [Fact]
    public async Task A_root_whose_scan_failed_shows_its_last_scan_as_failed()
    {
        await TestLibrary.AddRootFolderAsync(
            GetAppService<IDocumentStore>(), Path.Combine(Path.GetTempPath(), "debarr-missing"), CancellationToken, scannedAt: DateTimeOffset.UtcNow, scanError: "The folder is missing.");

        var cut = RenderPage<StatusPage>();

        cut.WaitForAssertion(() => Assert.StartsWith("Failed ", cut.Find(".status-root-folder-last-scan").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task The_health_section_and_the_badge_update_in_place_when_a_check_finds_a_problem_gone()
    {
        var cut = RenderPage<StatusPage>();
        cut.WaitForAssertion(
            () =>
            {
                Assert.Contains("No root folder is enabled", cut.Find(".status-health-message").TextContent);
                Assert.Equal("settings/library", cut.Find(".status-health-message a").GetAttribute("href"));
                Assert.StartsWith("1", cut.Find("#health-badge").TextContent.Trim());
            },
            Timeout);

        var root = Directory.CreateTempSubdirectory("debarr-root-");
        try
        {
            await GetAppService<IWolverineRuntime>().SendCommandAsync(new AddRootFolder(new LocalPath(root.FullName), DateTimeOffset.UtcNow), CancellationToken);

            cut.WaitForAssertion(
                () =>
                {
                    Assert.Equal("No problems found.", cut.Find("#status-health-none").TextContent.Trim());
                    Assert.Empty(cut.FindAll("#health-badge"));
                },
                Timeout);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_detection_counts_count_each_video_file_once_whatever_its_path_count()
    {
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        var store = GetAppService<IDocumentStore>();
        await TestVideoFile.AddAsync(store, ["/media/a.mkv", "/backup/a.mkv"], CancellationToken);
        await TestVideoFile.AddAsync(store, "/media/b.mkv", CancellationToken);
        var failed = await TestVideoFile.AddAsync(store, ["/media/c.mkv", "/backup/c.mkv"], CancellationToken);
        await TestVideoFile.AppendAsync(store, failed, [new DetectionFailed(failed, TestVideoFile.Failed("ffprobe exited 1."))], CancellationToken);

        var cut = RenderPage<StatusPage>();

        cut.WaitForAssertion(() => Assert.Equal("2 pending", cut.Find("#status-pending").TextContent.Trim()), Timeout);
        Assert.Equal("1 failed", cut.Find("#status-failed").TextContent.Trim());
    }

    [Fact]
    public async Task The_detection_counts_update_when_a_standard_ratios_change_returns_a_result_to_the_queue()
    {
        await TestVideoFile.AddAsync(
            GetAppService<IDocumentStore>(),
            "/media/flat.mkv",
            CancellationToken,
            followedBy: videoFile => [new AspectRatioDetected(videoFile, TestVideoFile.Detected(1.85, AspectRatioSource.FromFile, containerAspectRatio: 1.85))]);
        var cut = RenderPage<StatusPage>();
        cut.WaitForAssertion(() => Assert.Equal("0 pending", cut.Find("#status-pending").TextContent.Trim()), Timeout);

        await SendChangeDetectionSettingsAsync(settings => settings with
        {
            StandardRatios = [.. settings.StandardRatios.Select(ratio => ratio.AspectRatio == 1.85 ? ratio with { ChecksPicture = true } : ratio)],
        });

        cut.WaitForAssertion(() => Assert.Equal("1 pending", cut.Find("#status-pending").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task A_root_folder_the_library_dropped_shows_as_removing_until_its_removal_ends()
    {
        var root = Path.Combine(Path.GetTempPath(), "debarr-removing");
        await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.39);
        var cut = RenderPage<StatusPage>();
        cut.WaitForElement("#status-root-folders-empty", Timeout);

        await WhileRootFolderRemovalRunsAsync(root, () =>
        {
            cut.WaitForAssertion(() => Assert.StartsWith("Removing for ", cut.Find("#status-root-folder-removing .status-root-folder-last-scan").TextContent.Trim()), Timeout);
            Assert.Contains(root, cut.Find("#status-root-folder-removing").TextContent);
            Assert.Empty(cut.FindAll("#status-root-folders-empty"));
            StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#status-root-folders"));
        });

        cut.WaitForElement("#status-root-folders-empty", Timeout);
        Assert.Empty(cut.FindAll("#status-root-folder-removing"));
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_its_players_playbacks_notifiers_or_root_folders()
    {
        var store = GetAppService<IDocumentStore>();
        var cut = RenderPage<StatusPage>();
        cut.WaitForElement("#status-players-empty", Timeout);

        var playerId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(Players.StreamId, new PlayerAdded(playerId, "Bedroom", false, KodiEndpoint.Create("kodi.local", 9090, 60, 10).Value, [], []));
            await session.SaveChangesAsync(CancellationToken);
        }

        cut.WaitForAssertion(() => Assert.Equal("No playback yet", cut.Find(".status-player-last-playback").TextContent.Trim()), Timeout);

        await SeedPlaybacksAsync(TestPlayback.Handled("Bedroom", "Heat", playerId: playerId));
        cut.WaitForAssertion(() => Assert.Contains("Heat", cut.Find(".status-player-last-playback").TextContent), Timeout);

        await ClearHistoryAsync();
        cut.WaitForAssertion(() => Assert.Equal("No playback yet", cut.Find(".status-player-last-playback").TextContent.Trim()), Timeout);

        var notifier = new SaveNotifier(Guid.NewGuid(), "Lights", false, WebhookSettings.Create("http://localhost:9/", WebhookMethod.Post, []).Value);
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(notifier, CancellationToken)).IsSuccess);
        cut.WaitForAssertion(() => Assert.Contains("Lights", cut.Find(".status-notifier").TextContent), Timeout);

        var root = Path.Combine(Path.GetTempPath(), "debarr-disabled");
        await TestLibrary.AddRootFolderAsync(store, root, CancellationToken, enabled: false);
        cut.WaitForAssertion(() => Assert.Contains(root, cut.Find(".status-root-folder").TextContent), Timeout);

        var scannedAt = DateTimeOffset.UtcNow;
        await TestLibrary.AppendLibraryScanAsync(
            store,
            Guid.CreateVersion7(),
            [new LibraryScanStarted(scannedAt), new RootFolderScanned(new LocalPath(root), scannedAt, "The folder is missing.")],
            CancellationToken);
        cut.WaitForAssertion(() => Assert.StartsWith("Failed ", cut.Find(".status-root-folder-last-scan").TextContent.Trim()), Timeout);
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#status-root-folders"));
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#status-players"));
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#status-notifiers"));
    }

    [Fact]
    public void About_shows_the_ffmpeg_and_ffprobe_versions()
    {
        var cut = RenderPage<StatusPage>();

        cut.WaitForAssertion(() => Assert.Matches(@"\d", cut.Find("#status-ffprobe").TextContent), Timeout);
        Assert.Matches(@"\d", cut.Find("#status-ffmpeg").TextContent);
    }
}
