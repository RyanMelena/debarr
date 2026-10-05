using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Extensions;
using Debarr.Tests.Scanning;
using Fisher;
using FluentResults;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Detecting;

public sealed class DetectionOrchestratorTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime WrittenAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _timeProvider = new(Start);
    private readonly DetectorDouble _detector = new();
    private readonly FakeLoggerProvider _logs = new();
    private readonly PauseBeforeRecord _pauseBeforeRecord = new();
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;
    private DetectionOrchestrator _orchestrator = null!;

    // How long a wait on the app's own work may take while the full suite loads the machine.
    private static readonly TimeSpan LoadMargin = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(
            services => services
                .AddSingleton<TimeProvider>(_timeProvider)
                .AddSingleton<IAspectRatioDetector>(_detector)
                .ConfigureFisher(options => options.Listeners.Add(_pauseBeforeRecord)),
            logging => logging.AddProvider(_logs).AddFilter<FakeLoggerProvider>("Debarr", LogLevel.Debug));
        _scanner = _host.Services.GetRequiredService<LibraryScanner>();
        _orchestrator = _host.Services.GetRequiredService<DetectionOrchestrator>();
        Directory.CreateDirectory(Root);

        await TestLibrary.AddRootFolderAsync(_host.Services.GetRequiredService<IDocumentStore>(), Root, CancellationToken);
        await _host.Scheduler.Start(CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _detector.ReleaseAll();
        await _orchestrator.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task The_newest_video_file_is_detected_first_and_each_queue_check_starts_the_next()
    {
        await SetSimultaneousDetectionsAsync(1);
        var (alpha, bravo, charlie) = await AddInOrderAsync("alpha.mkv", "bravo.mkv", "charlie.mkv");

        await StartAsync();

        foreach (var videoFile in new[] { charlie, bravo })
        {
            await WaitUntilDetectedAsync(videoFile.VideoFile);
            await WaitUntilNoneRunningAsync();
            AdvanceToNextQueueCheck();
        }

        await WaitUntilDetectedAsync(alpha.VideoFile);
        Assert.Equal([charlie.Path, bravo.Path, alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_video_file_a_scan_adds_is_detected_at_the_next_queue_check_and_the_detection_is_stored()
    {
        await StartAsync();

        var path = Write("alpha.mkv");
        await _scanner.LibraryScanAsync(CancellationToken);
        AdvanceToNextQueueCheck();

        var videoFile = await ReadVideoFileAsync(path);
        await WaitUntilDetectedAsync(videoFile);
        var row = await ReadVideoFileAsync(videoFile);
        var detection = Assert.Single(await ReadDetectionsAsync(videoFile));
        Assert.Equal((detection.Id, (Guid?)null), (row.CurrentResult?.Id, row.LastFailure?.Id));
        Assert.Equal(
            (DetectionOrigin.Queue, path, Start + DetectionOrchestrator.QueueCheckInterval, TimeSpan.Zero, 1, "8.1.2"),
            (detection.Origin, detection.Path?.Value, detection.StartedAt, detection.Duration, detection.DetectorVersion, detection.FfmpegVersion));
        var (result, metadata) = (Assert.IsType<DetectionOutcome.Succeeded>(detection.Outcome).Result, detection.ContainerMetadata!);
        Assert.Equal(
            (2.4, AspectRatioSource.Detected, 1.0, 1920.0 / 1080, 1920, 1080, "hevc", "bt709"),
            (result.RawAspectRatio.Value, result.AspectRatioSource, result.Confidence, metadata.ContainerAspectRatio.Value,
                metadata.Width, metadata.Height, metadata.CodecName, metadata.ColorTransfer));
        Assert.Equal([new CropSample(TimeSpan.FromSeconds(30), new CropBox(1920, 800))], result.Samples);
    }

    [Fact]
    public async Task A_video_file_added_while_every_detection_slot_of_the_detection_queue_is_busy_starts_at_the_first_queue_check_after_a_detection_ends()
    {
        _detector.Holds = true;
        await SetSimultaneousDetectionsAsync(1);
        await StartAsync();
        var alpha = await AddAsync("alpha.mkv");
        AdvanceToNextQueueCheck();
        await Poll.UntilAsync(() => _detector.Running == 1);

        var bravo = await AddAsync("bravo.mkv");
        AdvanceToNextQueueCheck();
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);
        Assert.Equal([alpha.Path], _detector.Paths);

        _detector.Release(alpha.Path);
        await WaitUntilNoneRunningAsync();
        AdvanceToNextQueueCheck();

        await Poll.UntilAsync(() => _detector.Paths.Count == 2);
        Assert.Equal([alpha.Path, bravo.Path], _detector.Paths);
    }

    // Each queue check starts two video files, and their detections reach the detector in either order.
    [Fact]
    public async Task No_more_than_the_simultaneous_detections_run_and_a_saved_number_applies_from_the_next_queue_check()
    {
        _detector.Holds = true;
        await SetSimultaneousDetectionsAsync(2);
        var (alpha, bravo, charlie) = await AddInOrderAsync("alpha.mkv", "bravo.mkv", "charlie.mkv");
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var delta = await AddAsync("delta.mkv");

        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 2);
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);
        Assert.Equal([charlie.Path, delta.Path], _detector.Paths.Order(StringComparer.Ordinal));

        await SetSimultaneousDetectionsAsync(3);
        _detector.Release(delta.Path);
        await Poll.UntilAsync(() => _orchestrator.RunningDetections.Count == 1);
        AdvanceToNextQueueCheck();

        await Poll.UntilAsync(() => _detector.Paths.Count == 4);
        Assert.Equal([alpha.Path, bravo.Path], _detector.Paths.Skip(2).Order(StringComparer.Ordinal));
        Assert.Equal(3, _detector.Running);
    }

    [Fact]
    public async Task A_detection_a_pause_cancels_writes_nothing_leaves_the_running_list_and_runs_again_at_the_next_queue_check()
    {
        _detector.Holds = true;
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        var pause = await _orchestrator.PauseDetectionsAsync(CancellationToken);

        Assert.Empty(_orchestrator.RunningDetections);
        Assert.Empty(await ReadDetectionsAsync(alpha.VideoFile));
        pause.Dispose();
        AdvanceToNextQueueCheck();
        await Poll.UntilAsync(() => _detector.Paths.Count == 2);
        Assert.Equal(
            [new RunningDetection(alpha.VideoFile, new LocalPath(alpha.Path), DetectionOrigin.Queue, Start + DetectionOrchestrator.QueueCheckInterval)],
            _orchestrator.RunningDetections);

        _detector.Release(alpha.Path);
        await WaitUntilDetectedAsync(alpha.VideoFile);
        await WaitUntilNoneRunningAsync();
    }

    [Fact]
    public async Task A_detection_logs_at_debug_in_its_scope_when_it_starts_and_when_a_pause_cancels_it()
    {
        _detector.Holds = true;
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        using (await _orchestrator.PauseDetectionsAsync(CancellationToken))
        {
            var logs = _logs.Collector.GetSnapshot();
            var started = Assert.Single(logs, log => log.Message == $"Started detecting {alpha.Path}.");
            var cancelled = Assert.Single(logs, log => log.Message == $"Cancelled the detection of {alpha.Path}.");
            Assert.All([started, cancelled], log =>
            {
                Assert.Equal(LogLevel.Debug, log.Level);
                Assert.Contains(log.Scopes, state => state?.ToString() == $"Detection of {alpha.VideoFile}");
            });
        }

        _detector.ReleaseAll();
    }

    [Fact]
    public async Task A_pause_that_cancels_a_detection_while_its_record_commits_records_nothing_and_logs_no_error()
    {
        var alpha = await AddAsync("alpha.mkv");
        Task<DetectionPause>? pausing = null;
        _pauseBeforeRecord.PauseOnce(() => pausing = _orchestrator.PauseDetectionsAsync(CancellationToken));

        await StartAsync();
        await Poll.UntilAsync(() => pausing is not null);
        using (await pausing!)
        {
            Assert.Empty(await ReadDetectionsAsync(alpha.VideoFile));
            var logs = _logs.Collector.GetSnapshot();
            Assert.DoesNotContain(logs, log => log.Level >= LogLevel.Error || log.Exception is OperationCanceledException);
            Assert.Contains(logs, log => log.Level == LogLevel.Debug && log.Message.StartsWith($"RecordDetection of {alpha.Path} was cancelled after ", StringComparison.Ordinal));
            Assert.Contains(logs, log => log.Message == $"Cancelled the detection of {alpha.Path}.");
        }
    }

    [Fact]
    public async Task A_detection_that_throws_leaves_the_running_list_and_its_video_file_runs_again()
    {
        var alpha = await AddAsync("alpha.mkv");
        _detector.Respond = _ =>
        {
            _detector.Respond = _ => DetectorDouble.Letterboxed240;
            throw new OperationCanceledException();
        };

        await StartAsync();
        await Poll.UntilAsync(() => _detector.Paths.Count == 1);
        await WaitUntilNoneRunningAsync();
        AdvanceToNextQueueCheck();

        await WaitUntilDetectedAsync(alpha.VideoFile);
        Assert.Equal([alpha.Path, alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task Each_detection_publishes_its_start_and_its_end_with_its_file_path()
    {
        var events = new ConcurrentQueue<ActivityEvent>();
        using var subscription = _orchestrator.ActivityEvents.Subscribe(events.Enqueue);
        var alpha = await AddAsync("alpha.mkv");

        await StartAsync();

        await Poll.UntilAsync(() => events.Count == 2);
        Assert.Equal([new DetectionStartedEvent(alpha.VideoFile, new LocalPath(alpha.Path)), new DetectionFinishedEvent(alpha.VideoFile, new LocalPath(alpha.Path))], events);
    }

    [Fact]
    public async Task What_a_detection_logs_carries_its_scope_through_to_its_record_detection_command()
    {
        _detector.Respond = _ => throw new InvalidOperationException("The detector broke.");
        var alpha = await AddAsync("alpha.mkv");
        var recorded = _host.Services.GetRequiredService<ReadModelChangeListener>().Committed<DetectionFailed>().FirstAsync().ToTask(CancellationToken);

        await StartAsync();
        await recorded.WaitAsync(LoadMargin, CancellationToken);

        // The command logs its line once its commit returns.
        await Poll.UntilAsync(() => _logs.Collector.GetSnapshot().Any(log => log.GetStructuredStateValue("Command") == nameof(RecordDetection)), LoadMargin);
        var scope = $"Detection of {alpha.VideoFile}";
        var logs = _logs.Collector.GetSnapshot();
        var detectorFailed = Assert.Single(logs, log => log.Exception?.Message == "The detector broke.");
        var commandLine = Assert.Single(logs, log => log.GetStructuredStateValue("Command") == nameof(RecordDetection));
        Assert.Contains(detectorFailed.Scopes, state => state?.ToString() == scope);
        Assert.Contains(commandLine.Scopes, state => state?.ToString() == scope);
    }

    [Fact]
    public async Task A_failed_detection_is_recorded_once_and_not_retried()
    {
        _detector.Respond = _ => new DetectorOutcome("8.1.2", null, Result.Fail("ffprobe exited with code 1: Invalid data found when processing input"));
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await WaitUntilFailedAsync(alpha.VideoFile);

        _detector.Respond = _ => DetectorDouble.Letterboxed240;
        var bravo = await AddAsync("bravo.mkv");
        await WaitUntilNoneRunningAsync();
        AdvanceToNextQueueCheck();
        await WaitUntilDetectedAsync(bravo.VideoFile);
        AdvanceToNextQueueCheck();
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);

        var failed = await ReadVideoFileAsync(alpha.VideoFile);
        var detection = Assert.Single(await ReadDetectionsAsync(alpha.VideoFile));
        Assert.Equal((detection.Id, (Guid?)null), (failed.LastFailure?.Id, failed.CurrentResult?.Id));
        Assert.Equal(
            ("ffprobe exited with code 1: Invalid data found when processing input", Start, "8.1.2", (double?)null, (double?)null),
            (detection.Error, detection.StartedAt, detection.FfmpegVersion, detection.ContainerMetadata?.ContainerAspectRatio.Value, detection.Result?.RawAspectRatio.Value));
        Assert.Equal([alpha.Path, bravo.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_failed_detect_now_keeps_the_existing_detection_result()
    {
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await WaitUntilDetectedAsync(alpha.VideoFile);
        await WaitUntilNoneRunningAsync();

        _timeProvider.Advance(TimeSpan.FromHours(1));
        _detector.Respond = _ => DetectorDouble.Failed("timed out after 900s (3/12 samples)");
        Assert.True((await _orchestrator.DetectNowAsync(alpha.VideoFile, CancellationToken)).IsSuccess);
        await WaitUntilFailedAsync(alpha.VideoFile);

        var videoFile = await ReadVideoFileAsync(alpha.VideoFile);
        var detections = await ReadDetectionsAsync(alpha.VideoFile);
        Assert.Equal(2, detections.Count);
        var (failed, detected) = (detections[0], detections[1]);
        Assert.Equal((detected.Id, failed.Id), (videoFile.CurrentResult?.Id, videoFile.LastFailure?.Id));
        Assert.Equal((2.4, AspectRatioSource.Detected, Start), (detected.Result!.RawAspectRatio.Value, detected.Result!.AspectRatioSource, detected.StartedAt));
        Assert.Equal(
            (DetectionOrigin.DetectNow, "timed out after 900s (3/12 samples)", Start.AddHours(1), (double?)null),
            (failed.Origin, failed.Error, failed.StartedAt, failed.Result?.RawAspectRatio.Value));
        Assert.Equal(
            (1920.0 / 1080, 1920, 1080, "hevc", "bt709", "8.1.2"),
            (failed.ContainerMetadata!.ContainerAspectRatio.Value, failed.ContainerMetadata.Width, failed.ContainerMetadata.Height, failed.ContainerMetadata.CodecName, failed.ContainerMetadata.ColorTransfer, failed.FfmpegVersion));
    }

    [Fact]
    public async Task Detect_now_replaces_an_existing_detection_result_and_clears_the_last_failure()
    {
        _detector.Respond = _ => DetectorDouble.Failed("no crop detected in 12 samples");
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await WaitUntilFailedAsync(alpha.VideoFile);
        await WaitUntilNoneRunningAsync();

        _timeProvider.Advance(TimeSpan.FromHours(1));
        _detector.Respond = _ => DetectorDouble.Letterboxed240 with { Result = new DetectionResult(AspectRatioSource.FromFile, new AspectRatio(1.85), 0.9, []) };
        Assert.True((await _orchestrator.DetectNowAsync(alpha.VideoFile, CancellationToken)).IsSuccess);
        await WaitUntilDetectedAsync(alpha.VideoFile);

        var videoFile = await ReadVideoFileAsync(alpha.VideoFile);
        var detections = await ReadDetectionsAsync(alpha.VideoFile);
        Assert.Equal(2, detections.Count);
        var detected = detections[0];
        Assert.Equal((detected.Id, (Guid?)null), (videoFile.CurrentResult?.Id, videoFile.LastFailure?.Id));
        Assert.Equal(
            (DetectionOrigin.DetectNow, 1.85, AspectRatioSource.FromFile, 0, Start.AddHours(1)),
            (detected.Origin, detected.Result!.RawAspectRatio.Value, detected.Result!.AspectRatioSource, detected.Result!.Samples.Count, detected.StartedAt));
    }

    [Fact]
    public async Task Detect_now_runs_while_every_detection_slot_of_the_detection_queue_is_busy_and_refuses_a_second_request()
    {
        _detector.Holds = true;
        await SetSimultaneousDetectionsAsync(1);
        var (alpha, bravo, charlie) = await AddInOrderAsync("alpha.mkv", "bravo.mkv", "charlie.mkv");

        // The copy is hashed last, so a detection of alpha tries it first.
        var alphaCopy = Path.Combine(Root, "alpha copy.mkv");
        File.Copy(alpha.Path, alphaCopy);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await _scanner.ScanFileAsync(new LocalPath(alphaCopy), CancellationToken);
        var startedAt = _timeProvider.GetUtcNow();
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        var queueFileRefused = await _orchestrator.DetectNowAsync(charlie.VideoFile, CancellationToken);
        var started = await _orchestrator.DetectNowAsync(alpha.VideoFile, CancellationToken);
        var secondRefused = await _orchestrator.DetectNowAsync(bravo.VideoFile, CancellationToken);
        await Poll.UntilAsync(() => _detector.Running == 2);

        Assert.Equal(["The video file is already being detected."], queueFileRefused.Errors.Select(error => error.Message));
        Assert.True(started.IsSuccess);
        Assert.Equal(["Detect Now is already running on another video file."], secondRefused.Errors.Select(error => error.Message));
        Assert.Equal([charlie.Path, alphaCopy], _detector.Paths);
        Assert.Equal(
            [
                new RunningDetection(alpha.VideoFile, new LocalPath(alphaCopy), DetectionOrigin.DetectNow, startedAt),
                new RunningDetection(charlie.VideoFile, new LocalPath(charlie.Path), DetectionOrigin.Queue, startedAt),
            ],
            _orchestrator.RunningDetections.OrderByDescending(running => running.Origin));
    }

    [Fact]
    public async Task The_detection_queue_skips_a_video_file_that_detect_now_is_running()
    {
        _detector.Holds = true;
        await SetSimultaneousDetectionsAsync(1);
        var (alpha, bravo, charlie) = await AddInOrderAsync("alpha.mkv", "bravo.mkv", "charlie.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);
        Assert.True((await _orchestrator.DetectNowAsync(bravo.VideoFile, CancellationToken)).IsSuccess);
        await Poll.UntilAsync(() => _detector.Running == 2);

        await SetSimultaneousDetectionsAsync(2);
        _detector.Release(charlie.Path);
        await Poll.UntilAsync(() => _orchestrator.RunningDetections.Count == 1);
        AdvanceToNextQueueCheck();

        // The queue check passes over bravo, the newest pending video file, and starts alpha.
        await Poll.UntilAsync(() => _detector.Paths.Count == 3);
        Assert.Equal([charlie.Path, bravo.Path, alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_detection_falls_back_to_the_next_file_path_when_one_cannot_be_opened()
    {
        var alpha = await AddAsync("alpha.mkv");
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        var copy = Path.Combine(Root, "copy.mkv");
        File.Copy(alpha.Path, copy);
        File.SetLastWriteTimeUtc(copy, WrittenAt);
        await _scanner.ScanFileAsync(new LocalPath(copy), CancellationToken);

        // The copy was hashed last, so it is tried first.
        using (new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await StartAsync();
            await WaitUntilDetectedAsync(alpha.VideoFile);
        }

        Assert.Equal([alpha.Path], _detector.Paths);
        Assert.Equal(alpha.VideoFile, await ReadVideoFileAsync(copy));
    }

    [Fact]
    public async Task A_path_whose_modification_time_changed_is_scanned_again_and_detected_at_the_next_queue_check()
    {
        var alpha = await AddAsync("alpha.mkv");
        var touchedAt = WrittenAt.AddDays(1);
        File.SetLastWriteTimeUtc(alpha.Path, touchedAt);

        // The first detection scans the path and ends, since no unchanged path is left.
        await StartAsync();
        await Poll.UntilAsync(async () => (await ReadModifiedAtAsync(alpha.Path)) == touchedAt);
        await WaitUntilNoneRunningAsync();
        Assert.Empty(_detector.Paths);
        AdvanceToNextQueueCheck();

        await WaitUntilDetectedAsync(alpha.VideoFile);
        var filePath = (await TestVideoFile.ReadStoredFilePathAsync(_host.Services.GetRequiredService<IDocumentStore>(), alpha.Path, CancellationToken))!;
        Assert.Equal((alpha.VideoFile, new DateTimeOffset(touchedAt)), (filePath.VideoFile, filePath.Stat.ModifiedAt));
        Assert.Equal([alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_file_that_changes_during_its_detection_has_the_result_discarded_and_its_new_content_detected()
    {
        var alpha = await AddAsync("alpha.mkv");
        _detector.Respond = path =>
        {
            _detector.Respond = _ => DetectorDouble.Letterboxed240;
            File.AppendAllText(path, " re-encoded");
            return DetectorDouble.Letterboxed240;
        };

        await StartAsync();

        // The path leaves alpha before it joins the new content's video file.
        await Poll.UntilAsync(async () => await TryReadVideoFileAsync(alpha.Path) is { } current && current != alpha.VideoFile);
        var reencoded = await ReadVideoFileAsync(alpha.Path);
        await WaitUntilNoneRunningAsync();
        AdvanceToNextQueueCheck();
        await WaitUntilDetectedAsync(reencoded);
        Assert.Empty(await ReadDetectionsAsync(alpha.VideoFile));
        Assert.Equal([alpha.Path, alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_video_file_with_no_file_paths_stays_out_of_the_detection_queue()
    {
        var events = new ConcurrentQueue<ActivityEvent>();
        using var subscription = _orchestrator.ActivityEvents.OfType<DetectionStartedEvent>().Subscribe(events.Enqueue);
        var alpha = await AddAsync("alpha.mkv");
        var bravo = await AddAsync("bravo.mkv");
        File.Delete(bravo.Path);
        await _scanner.ScanFileAsync(new LocalPath(bravo.Path), CancellationToken);

        await StartAsync();

        await WaitUntilDetectedAsync(alpha.VideoFile);
        Assert.Equal([new DetectionStartedEvent(alpha.VideoFile, new LocalPath(alpha.Path))], events);
        Assert.Empty(await ReadDetectionsAsync(bravo.VideoFile));
        Assert.Equal(
            ["The video file has no file path to detect."],
            (await _orchestrator.DetectNowAsync(bravo.VideoFile, CancellationToken)).Errors.Select(error => error.Message));
    }

    [Fact]
    public async Task Nothing_starts_during_a_detection_pause_and_the_next_queue_check_after_it_starts_the_detection_queue()
    {
        var alpha = await AddAsync("alpha.mkv");
        var pause = await _orchestrator.PauseDetectionsAsync(CancellationToken);

        await StartAsync();
        AdvanceToNextQueueCheck();
        var refused = await _orchestrator.DetectNowAsync(alpha.VideoFile, CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);

        Assert.Equal(["Detection is paused. It resumes on its own, so try again shortly."], refused.Errors.Select(error => error.Message));
        Assert.Empty(_detector.Paths);
        pause.Dispose();
        AdvanceToNextQueueCheck();
        await WaitUntilDetectedAsync(alpha.VideoFile);
    }

    [Fact]
    public async Task A_pause_resumes_once_however_often_it_is_disposed_and_pauses_run_one_at_a_time()
    {
        await StartAsync();
        var pause = await _orchestrator.PauseDetectionsAsync(CancellationToken);
        pause.Dispose();
        pause.Dispose();

        var first = await _orchestrator.PauseDetectionsAsync(CancellationToken);
        var second = _orchestrator.PauseDetectionsAsync(CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken)).Dispose();
    }

    [Fact]
    public async Task Stopping_cancels_the_running_detections_and_ends_the_queue_checks()
    {
        _detector.Holds = true;
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        await _orchestrator.StopAsync(CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        AdvanceToNextQueueCheck();
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);

        Assert.Empty(_orchestrator.RunningDetections);
        Assert.Empty(await ReadDetectionsAsync(alpha.VideoFile));
        Assert.Equal([alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_root_folder_removal_cancels_the_detections_under_it_starts_none_while_it_runs_and_the_detection_queue_resumes_after_it()
    {
        _detector.Holds = true;
        await SetSimultaneousDetectionsAsync(1);
        var bravo = await AddAsync("bravo.mkv", await AddOtherRootFolderAsync());
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);
        await TestLibrary.AppendAsync(_host.Services.GetRequiredService<IDocumentStore>(), [new RootFolderDisabled(new LocalPath(Root))], CancellationToken);

        Task<int> removal;
        await using (await HoldWriteLockAsync())
        {
            removal = Task.Run(() => _host.Services.GetRequiredService<RootFolderRemover>().RemoveAsync(new DirectoryInfo(Root), CancellationToken), CancellationToken);
            await Poll.UntilAsync(() => _detector.Running == 0);
            AdvanceToNextQueueCheck();
            AdvanceToNextQueueCheck();
            await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);

            Assert.Equal([alpha.Path], _detector.Paths);
            Assert.Empty(_orchestrator.RunningDetections);
            Assert.False(removal.IsCompleted, "the removal should wait on the write lock");
        }

        Assert.Equal(1, await removal);
        Assert.Empty(await ReadDetectionsAsync(alpha.VideoFile));
        AdvanceToNextQueueCheck();
        await Poll.UntilAsync(() => _detector.Paths.Count == 2);
        Assert.Equal([alpha.Path, bravo.Path], _detector.Paths);
        _detector.Release(bravo.Path);
        await WaitUntilDetectedAsync(bravo.VideoFile);
    }

    [Fact]
    public async Task A_failed_root_folder_removal_ends_its_detection_pause_and_the_detection_it_cancelled_runs_again()
    {
        _detector.Holds = true;
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        using var removalCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        Task<int> removal;
        await using (await HoldWriteLockAsync())
        {
            removal = Task.Run(() => _host.Services.GetRequiredService<RootFolderRemover>().RemoveAsync(new DirectoryInfo(Root), removalCancellation.Token), CancellationToken);
            await Poll.UntilAsync(() => _detector.Running == 0);
            await removalCancellation.CancelAsync();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => removal);
        AdvanceToNextQueueCheck();
        await Poll.UntilAsync(() => _detector.Paths.Count == 2);
        Assert.Equal([alpha.Path, alpha.Path], _detector.Paths);
    }

    private Task StartAsync() => _orchestrator.StartAsync(CancellationToken);

    /// <summary>
    /// Starts a detection pause once, as a session that records a detection is about to write,
    /// and holds the write until the pause has cancelled the detection's token.
    /// </summary>
    private sealed class PauseBeforeRecord : IDocumentSessionListener
    {
        private Action? _pause;

        public void PauseOnce(Action pause) => _pause = pause;

        public async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            var records = ((JasperFx.Events.Documents.IDocumentSessionOperations)session).PendingStreams
                .SelectMany(stream => stream.Events)
                .Any(pending => pending.Data is AspectRatioDetected or DetectionFailed);
            if (records && Interlocked.Exchange(ref _pause, null) is { } pause)
            {
                pause();
                await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        public Task AfterCommitAsync(IDocumentSession session, Fisher.Services.IChangeSet commit, CancellationToken token) => Task.CompletedTask;
    }

    /// <summary>Moves the clock to the next queue check, which then runs.</summary>
    private void AdvanceToNextQueueCheck() => _timeProvider.Advance(DetectionOrchestrator.QueueCheckInterval);

    /// <summary>Writes a file named for its content, under <see cref="Root"/> unless a folder is given, and scans it.</summary>
    private async Task<(string Path, FileHash VideoFile)> AddAsync(string name, string? folder = null)
    {
        var path = Write(name, folder);
        return (path, (await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken))!.Value);
    }

    /// <summary>Adds three files a millisecond apart, so the detection queue holds them newest first.</summary>
    private async Task<((string Path, FileHash VideoFile), (string Path, FileHash VideoFile), (string Path, FileHash VideoFile))> AddInOrderAsync(string first, string second, string third)
    {
        var added = new List<(string Path, FileHash VideoFile)>();
        foreach (var name in new[] { first, second, third })
        {
            if (added.Count > 0)
            {
                _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
            }

            added.Add(await AddAsync(name));
        }

        return (added[0], added[1], added[2]);
    }

    private string Write(string name, string? folder = null)
    {
        var path = Path.Combine(folder ?? Root, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, WrittenAt);
        return path;
    }

    private async Task<FileHash> ReadVideoFileAsync(string path) => (await TryReadVideoFileAsync(path))!.Value;

    /// <summary>The video file the path belongs to; null while it belongs to none.</summary>
    private async Task<FileHash?> TryReadVideoFileAsync(string path) =>
        (await TestVideoFile.ReadStoredFilePathAsync(_host.Services.GetRequiredService<IDocumentStore>(), path, CancellationToken))?.VideoFile;

    private async Task<DateTimeOffset> ReadModifiedAtAsync(string path) =>
        (await TestVideoFile.ReadStoredFilePathAsync(_host.Services.GetRequiredService<IDocumentStore>(), path, CancellationToken))!.Stat.ModifiedAt;

    private async Task<VideoFile> ReadVideoFileAsync(FileHash videoFile) =>
        (await TestVideoFile.ReadVideoFileAsync(_host.Services.GetRequiredService<IDocumentStore>(), videoFile, CancellationToken))!;

    /// <summary>The video file's detections, newest first.</summary>
    private Task<IReadOnlyList<Detection>> ReadDetectionsAsync(FileHash videoFile) =>
        TestVideoFile.ReadDetectionsAsync(_host.Services.GetRequiredService<IDocumentStore>(), videoFile, CancellationToken);

    private Task WaitUntilDetectedAsync(FileHash videoFile) =>
        Poll.UntilAsync(async () => (await ReadVideoFileAsync(videoFile)).CurrentResult?.Id is not null);

    private Task WaitUntilFailedAsync(FileHash videoFile) =>
        Poll.UntilAsync(async () => (await ReadVideoFileAsync(videoFile)).LastFailure?.Id is not null);

    /// <summary>Waits for every detection to leave the running list, which follows its last write.</summary>
    private Task WaitUntilNoneRunningAsync() => Poll.UntilAsync(() => _orchestrator.RunningDetections.Count == 0);

    private async Task SetSimultaneousDetectionsAsync(int simultaneousDetections)
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        await session.Events.AppendAtCurrentVersionAsync(
            DetectionSettings.StreamId,
            new DetectionSettingsChanged(simultaneousDetections, DetectionSettings.Default.PictureMeasurement, DetectionSettings.Default.TimeoutSeconds));
        await session.SaveChangesAsync(CancellationToken);
    }

    /// <summary>Adds a root folder beside <see cref="Root"/> and returns its path.</summary>
    private async Task<string> AddOtherRootFolderAsync()
    {
        var other = Path.Combine(_host.DataDirectory, "other");
        Directory.CreateDirectory(other);
        await TestLibrary.AddRootFolderAsync(_host.Services.GetRequiredService<IDocumentStore>(), other, CancellationToken);
        return other;
    }

    /// <summary>Opens a write transaction on the database, which holds back every other write until it is disposed.</summary>
    private async Task<SqliteConnection> HoldWriteLockAsync()
    {
        var connection = new SqliteConnection(_host.UnpooledConnectionString);
        await connection.OpenAsync(CancellationToken);
        await connection.BeginTransactionAsync(CancellationToken);
        return connection;
    }
}
