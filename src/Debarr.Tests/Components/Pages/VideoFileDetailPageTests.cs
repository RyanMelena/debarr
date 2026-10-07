using Bunit;
using Debarr.Components.Pages;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Debarr.Tests.Extensions;
using Debarr.Tests.Playing;
using Debarr.Tests.Scanning;
using Fisher;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace Debarr.Tests.Components.Pages;

public sealed class VideoFileDetailPageTests : PageTestContext
{
    private readonly DetectorDouble _detector;
    private readonly CommandRecorder _commands;

    public VideoFileDetailPageTests()
        : this(new DetectorDouble(), new CommandRecorder())
    {
    }

    private VideoFileDetailPageTests(DetectorDouble detector, CommandRecorder commands)
        : base(services =>
        {
            services.AddSingleton<IAspectRatioDetector>(detector);
            services.ConfigureFisher(options => options.Logger(commands));
        }) =>
        (_detector, _commands) = (detector, commands);

    /// <summary>The page's reads of the playbacks, one for each reload.</summary>
    private int PlaybackReads => _commands.Commands.Count(command => command.Contains("fi_doc_playbackrow", StringComparison.Ordinal));

    [Fact]
    public async Task An_override_shows_as_the_ratio_and_the_detection_result_as_detected()
    {
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.391, overrideAspectRatio: 2.0);

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Equal("2.00 (override)", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);
        Assert.StartsWith("2.39", cut.Find("#video-file-detected").TextContent.Trim());
    }

    [Fact]
    public async Task An_override_saved_on_the_page_is_stored_and_shown_as_the_ratio()
    {
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.391);
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        await cut.RaiseInputAsync("#video-file-override-ratio", "2.2", Timeout);
        await cut.RaiseInputAsync("#video-file-override-note", "  Open matte on the disc  ", Timeout);
        await cut.RaiseClickAsync("#video-file-override-save", Timeout);

        cut.WaitForAssertion(() => Assert.Equal("2.20 (override)", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);
        var stored = (await TestVideoFile.ReadMediaRowAsync(GetAppService<IDocumentStore>(), videoFile, CancellationToken))!.Override!;
        Assert.Equal((new AspectRatio(2.2), false, "Open matte on the disc"), (stored.AspectRatio, stored.DontSend, stored.Note));
    }

    [Fact]
    public async Task An_override_ratio_of_zero_is_refused_beneath_the_ratio_field()
    {
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.391);
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        await cut.RaiseInputAsync("#video-file-override-ratio", "0", Timeout);
        await cut.RaiseClickAsync("#video-file-override-save", Timeout);

        cut.WaitForAssertion(
            () => Assert.Contains("The ratio must be greater than 0.", cut.Find("#video-file-override-ratio").Closest(".mud-input-control")!.TextContent),
            Timeout);
        Assert.Empty(cut.FindAll("#video-file-override-error"));
    }

    [Fact]
    public async Task The_title_is_the_most_recently_hashed_path_and_the_file_paths_list_newest_first()
    {
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        var store = GetAppService<IDocumentStore>();
        var now = DateTimeOffset.UtcNow;
        var videoFile = await TestVideoFile.AddAsync(store, "/media/old.mkv", CancellationToken, hashedAt: now);
        await TestVideoFile.AppendAsync(store, videoFile, [new FilePathAdded(videoFile, new LocalPath("/backup/new.mkv"), TestVideoFile.Stat, now.AddMinutes(1), now.AddMinutes(1))], CancellationToken);

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Equal("new.mkv", cut.Find("#page-title").TextContent.Trim()), Timeout);
        Assert.Equal(["/backup/new.mkv", "/media/old.mkv"], cut.FindAll(".video-file-path").Select(path => path.TextContent.Trim()));
    }

    [Fact]
    public void A_video_file_that_is_not_there_says_so_and_links_to_media()
    {
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = "12" });

        cut.WaitForElement("#video-file-missing", Timeout);
        Assert.Equal("Video File Not Found", cut.Find("#page-title").TextContent.Trim());
        Assert.Equal("Go to Media", cut.Find("#video-file-missing-media").TextContent.Trim());
    }

    [Fact]
    public async Task An_archived_video_file_shows_its_kept_result_override_and_detections_and_offers_no_change()
    {
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.391, overrideAspectRatio: 2.0);
        await AppendToVideoFileAsync(videoFile, new FilePathRemoved(videoFile, new LocalPath("/media/film.mkv"), DateTimeOffset.UtcNow));
        var archived = await GetAppService<IWolverineRuntime>().SendCommandAsync(new ArchiveVideoFiles([videoFile], DateTimeOffset.UtcNow), CancellationToken);
        Assert.True(archived.IsSuccess);

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Equal("Archived", cut.Find("#video-file-status .video-file-status").TextContent.Trim()), Timeout);
        Assert.Equal("film.mkv", cut.Find("#page-title").TextContent.Trim());
        Assert.Contains("restores them when a scan finds a file with its hash again", cut.Find("#video-file-status").TextContent);
        Assert.Equal("2.00 (override)", cut.Find("#video-file-ratio").TextContent.Trim());
        Assert.StartsWith("2.39", cut.Find("#video-file-detected").TextContent.Trim());
        Assert.Contains("Detections (1)", cut.Find("#video-file-tabs").TextContent);
        Assert.Contains("archived", cut.Find("#video-file-no-paths").TextContent);
        Assert.Empty(cut.FindAll("#video-file-detect-now"));
        Assert.Empty(cut.FindAll("#video-file-override-save"));
        Assert.Contains("applies again when a scan finds the file", cut.Find("#video-file-override").TextContent);
        Assert.True(cut.Find("#video-file-override-ratio").HasAttribute("disabled"));
        Assert.True(cut.Find("#video-file-override-note").HasAttribute("disabled"));
    }

    [Fact]
    public async Task An_archived_video_file_never_detected_from_a_path_is_titled_by_its_hash()
    {
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        var videoFile = await TestVideoFile.AddAsync(GetAppService<IDocumentStore>(), "/media/film.mkv", CancellationToken);
        await AppendToVideoFileAsync(videoFile, new FilePathRemoved(videoFile, new LocalPath("/media/film.mkv"), DateTimeOffset.UtcNow));
        Assert.True((await GetAppService<IWolverineRuntime>().SendCommandAsync(new ArchiveVideoFiles([videoFile], DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Equal(videoFile.Value, cut.Find("#page-title").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task A_video_file_restored_after_a_rebuild_while_it_was_archived_lists_every_detection()
    {
        using var pause = await GetAppService<DetectionOrchestrator>().PauseDetectionsAsync(CancellationToken);
        var store = GetAppService<IDocumentStore>();
        var runtime = GetAppService<IWolverineRuntime>();
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.391);
        await AppendToVideoFileAsync(
            videoFile,
            new DetectionFailed(videoFile, TestVideoFile.Failed("ffprobe exited 1: Invalid data found when processing input", "/media/film.mkv")),
            new FilePathRemoved(videoFile, new LocalPath("/media/film.mkv"), DateTimeOffset.UtcNow));
        Assert.True((await runtime.SendCommandAsync(new ArchiveVideoFiles([videoFile], DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);
        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(MediaRowProjection.ReadModel, CancellationToken);
            await daemon.RebuildProjectionAsync(StoredFilePathProjection.ReadModel, CancellationToken);
        }

        Assert.True((await runtime.SendCommandAsync(new UnarchiveVideoFile(videoFile), CancellationToken)).IsSuccess);
        Assert.True((await runtime.SendCommandAsync(new AddFilePath(videoFile, new LocalPath("/media/film.mkv"), TestVideoFile.Stat, DateTimeOffset.UtcNow), CancellationToken)).IsSuccess);

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Contains("Detections (2)", cut.Find("#video-file-tabs").TextContent), Timeout);
        Assert.Empty(cut.FindAll("#video-file-no-paths"));
        Assert.StartsWith("2.39", cut.Find("#video-file-ratio").TextContent.Trim());
    }

    [Fact]
    public async Task A_failed_detection_says_what_went_wrong_and_what_to_do_without_the_memory_address_or_path()
    {
        const string path = "/media/Broken/not a video.mkv";
        var (videoFile, _) = await SeedVideoFileAsync(path, 2.391);
        await AppendToVideoFileAsync(
            videoFile,
            new DetectionFailed(
                videoFile,
                TestVideoFile.Failed($"ffprobe exited 1: [matroska,webm @ 000001711537dec0] EBML header parsing failed\r\n{path}: Invalid data found when processing input", path)));

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForElement("#video-file-failure", Timeout);
        Assert.Contains("playbacks still send the earlier result", cut.Find("#video-file-failure").TextContent);
        Assert.StartsWith("ffprobe could not read the file. The file may be damaged", cut.Find("#video-file-failure-explanation").TextContent.Trim());
        Assert.Equal("EBML header parsing failed. Invalid data found when processing input.", cut.Find("#video-file-failure-detail").TextContent.Trim());
    }

    [Fact]
    public async Task A_link_to_a_detection_opens_the_detections_tab_on_that_detection()
    {
        var (videoFile, detection) = await SeedVideoFileAsync("/media/film.mkv", 2.391);

        var cut = RenderPage<VideoFileDetailPage>(
            $"video-file/{videoFile}#detection-{detection.Id}",
            new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Contains("video-file-detection-selected", cut.Find($"#detection-{detection.Id}").ClassName), Timeout);
    }

    [Theory]
    [InlineData("/media/film.mkv", false)]
    [InlineData("/media/old/film.mkv", true)]
    public async Task The_detections_path_column_shows_only_when_a_row_read_another_path(string detectionPath, bool showsPath)
    {
        var (videoFile, detection) = await SeedVideoFileAsync("/media/film.mkv", 2.391, detectionPath: detectionPath);

        var cut = RenderPage<VideoFileDetailPage>(
            $"video-file/{videoFile}#detection-{detection.Id}",
            new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForElement("#video-file-detections", Timeout);
        Assert.Equal(showsPath, cut.FindAll("#video-file-detections th").Any(header => header.TextContent.Trim() == "Path"));
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#video-file-detections"));
    }

    [Theory]
    [InlineData(2.353, AspectRatioSource.Detected, 1.778, "Detected, measured from the picture because the container ratio 1.778 matches 1.78, which is marked Check Picture")]
    [InlineData(2.391, AspectRatioSource.FromFile, 2.391, "From File, the ratio the file states, since 2.39 is not marked Check Picture")]
    [InlineData(2.12, AspectRatioSource.FromFile, 2.12, "From File, the ratio the file states, since the container ratio 2.120 matches no standard ratio")]
    public async Task The_source_says_why_the_picture_was_or_was_not_measured(double rawAspectRatio, AspectRatioSource source, double containerAspectRatio, string sourceText)
    {
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", rawAspectRatio);
        await AppendToVideoFileAsync(videoFile, new AspectRatioDetected(videoFile, TestVideoFile.Detected(rawAspectRatio, source, containerAspectRatio: containerAspectRatio)));

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForAssertion(() => Assert.Equal(sourceText, cut.Find("#video-file-source").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task Samples_that_differ_from_the_agreeing_ones_are_marked_and_the_agreement_is_counted()
    {
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.388);
        List<CropSample> samples =
        [
            new(TimeSpan.FromMinutes(1), new CropBox(1920, 804)),
            new(TimeSpan.FromMinutes(2), new CropBox(1920, 1080)),
            new(TimeSpan.FromMinutes(3), new CropBox(1920, 802)),
            new(TimeSpan.FromMinutes(4), null),
        ];
        await AppendToVideoFileAsync(videoFile, new AspectRatioDetected(videoFile, TestVideoFile.Detected(2.388, samples: samples)));

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForElement("#video-file-samples", Timeout);
        Assert.StartsWith("2 of 4 samples agree on a picture of about 1920 × 804.", cut.Find("#video-file-agreement").TextContent.Trim());
        Assert.Equal(
            ["0:02:00", "0:04:00"],
            cut.FindAll(".video-file-sample-differs td:first-child").Select(cell => cell.TextContent.Trim()));
        Assert.Contains("2 of 4 samples agree", cut.Find("#video-file-confidence").TextContent);
    }

    [Fact]
    public async Task The_page_reloads_when_a_commit_changes_its_video_file_the_settings_the_library_or_its_playbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "debarr-root");
        var (videoFile, _) = await SeedVideoFileAsync(Path.Combine(root, "film.mkv"), 2.12);
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });
        cut.WaitForAssertion(() => Assert.StartsWith("2.12 ", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);
        Assert.Empty(cut.FindAll(".video-file-root"));

        await SendChangeDetectionSettingsAsync(settings => settings with { StandardRatios = [.. settings.StandardRatios, new StandardRatio(2.10, false)] });
        cut.WaitForAssertion(() => Assert.StartsWith("2.10 ", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);

        await TestLibrary.AddRootFolderAsync(GetAppService<IDocumentStore>(), root, CancellationToken, enabled: false);
        cut.WaitForAssertion(() => Assert.Equal(root, cut.Find(".video-file-root a").TextContent.Trim()), Timeout);

        await SeedPlaybacksAsync(TestPlayback.Handled(videoFile: videoFile));
        cut.WaitForAssertion(() => Assert.Contains("Playbacks (1)", cut.Find("#video-file-tabs").TextContent), Timeout);

        await ClearHistoryAsync();
        cut.WaitForAssertion(() => Assert.Contains("Playbacks (0)", cut.Find("#video-file-tabs").TextContent), Timeout);

        await AppendToVideoFileAsync(videoFile, ManualOverride(2.0));
        cut.WaitForAssertion(() => Assert.Equal("2.00 (override)", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task A_commit_to_another_video_file_leaves_the_page_as_it_is()
    {
        var (videoFile, _) = await SeedVideoFileAsync("/media/film.mkv", 2.391);
        var (other, _) = await SeedVideoFileAsync("/media/other.mkv", 2.391);
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });
        cut.WaitForAssertion(() => Assert.StartsWith("2.39 ", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);
        var page = cut.FindComponent<VideoFileDetailPage>();
        var renders = page.RenderCount;

        await AppendToVideoFileAsync(other, ManualOverride(2.0));
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        Assert.Equal(renders, page.RenderCount);

        await AppendToVideoFileAsync(videoFile, ManualOverride(2.0));
        cut.WaitForAssertion(() => Assert.Equal("2.00 (override)", cut.Find("#video-file-ratio").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task Detect_Now_reads_Detecting_while_its_detection_runs_and_the_page_shows_the_detection_it_recorded()
    {
        var videoFile = await SeedFileAsync("film.mkv");
        _detector.Holds = true;
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });
        cut.WaitForAssertion(() => Assert.False(cut.Find("#video-file-detect-now").HasAttribute("disabled")), Timeout);

        await cut.RaiseClickAsync("#video-file-detect-now", Timeout);

        cut.WaitForAssertion(() => Assert.Equal("Detecting", cut.Find("#video-file-detect-now").TextContent.Trim()), Timeout);
        _detector.ReleaseAll();
        cut.WaitForAssertion(() => Assert.Contains("Detections (2)", cut.Find("#video-file-tabs").TextContent), Timeout);
        cut.WaitForAssertion(() => Assert.Equal("Detect Now", cut.Find("#video-file-detect-now").TextContent.Trim()), Timeout);
    }

    [Fact]
    public async Task Another_video_files_detection_leaves_the_page_as_it_is_while_detect_now_follows_it()
    {
        var videoFile = await SeedFileAsync("film.mkv");
        var other = await SeedFileAsync("other.mkv");
        _detector.Holds = true;
        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });
        cut.WaitForAssertion(() => Assert.False(cut.Find("#video-file-detect-now").HasAttribute("disabled")), Timeout);
        var reads = PlaybackReads;

        Assert.True((await GetAppService<DetectionOrchestrator>().DetectNowAsync(other, CancellationToken)).IsSuccess);

        cut.WaitForAssertion(() => Assert.True(cut.Find("#video-file-detect-now").HasAttribute("disabled")), Timeout);
        _detector.ReleaseAll();
        cut.WaitForAssertion(() => Assert.False(cut.Find("#video-file-detect-now").HasAttribute("disabled")), Timeout);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        Assert.Equal(reads, PlaybackReads);
    }

    [Fact]
    public async Task A_file_path_links_its_root_to_media_searched_for_the_files_under_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "debarr-root");
        await TestLibrary.AddRootFolderAsync(GetAppService<IDocumentStore>(), root, CancellationToken);
        var (videoFile, _) = await SeedVideoFileAsync(Path.Combine(root, "films", "film.mkv"), 2.391);

        var cut = RenderPage<VideoFileDetailPage>(parameters: new Dictionary<string, object?> { ["Hash"] = videoFile.Value });

        cut.WaitForElement("#video-file-paths", Timeout);
        var link = cut.Find("#video-file-paths td a");
        Assert.Equal(root, link.TextContent.Trim());
        Assert.Equal("?search=" + Uri.EscapeDataString(root + Path.DirectorySeparatorChar), link.GetAttribute("href"));
        StackedTableAssert.EachCellIsLabelledByItsColumn(cut.Find("#video-file-paths"));
    }

    private static OverrideSaved ManualOverride(double aspectRatio) =>
        new(Override.Create(aspectRatio, false, null, DateTimeOffset.UtcNow).Value);
}
