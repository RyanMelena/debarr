using System.Collections.Concurrent;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Scanning;
using Fisher;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Debarr.Tests.Detecting;

public sealed class DetectionCommandTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DetectedAt = Start.AddDays(-1);
    private static readonly DateTime WrittenAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _timeProvider = new(Start);
    private readonly DetectorDouble _detector = new();
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;
    private DetectionOrchestrator _orchestrator = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services
            .AddSingleton<TimeProvider>(_timeProvider)
            .AddSingleton<IAspectRatioDetector>(_detector));
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
    public async Task Raising_the_tolerance_clears_a_from_file_result_that_now_snaps_to_a_check_picture_ratio()
    {
        _detector.Holds = true;
        var nearWidescreen = await AddDetectedAsync("near-widescreen.mkv", AspectRatioSource.FromFile, 1.73);
        var scope = await AddDetectedAsync("scope.mkv", AspectRatioSource.FromFile, 2.39);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(matchTolerance: 0.05), await ReadStandardRatiosAsync()), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Null((await ReadVideoFileAsync(nearWidescreen.VideoFile)).CurrentResult?.Id);
        Assert.Equal((AspectRatioSource.FromFile, DetectedAt), await ReadSourceAndStartedAtAsync(scope.VideoFile));
    }

    [Fact]
    public async Task Lowering_the_tolerance_turns_a_detected_result_that_no_longer_snaps_to_a_check_picture_ratio_into_a_from_file_one()
    {
        _detector.Holds = true;
        var between = await AddDetectedAsync("between.mkv", AspectRatioSource.Detected, 1.81);
        var widescreen = await AddDetectedAsync("widescreen.mkv", AspectRatioSource.Detected, 1920.0 / 1080);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(matchTolerance: 0.02), await ReadStandardRatiosAsync()), CancellationToken);

        Assert.True(saved.IsSuccess);
        var converted = (await ReadVideoFileAsync(between.VideoFile)).CurrentResult!;
        Assert.Equal((1.81, AspectRatioSource.FromFile, Start), (converted.Result!.RawAspectRatio.Value, converted.Result!.AspectRatioSource, converted.StartedAt));
        Assert.Equal((AspectRatioSource.Detected, DetectedAt), await ReadSourceAndStartedAtAsync(widescreen.VideoFile));
    }

    [Fact]
    public async Task A_check_picture_change_re_checks_only_the_video_files_that_snap_to_that_ratio()
    {
        _detector.Holds = true;
        var academy = await AddDetectedAsync("academy.mkv", AspectRatioSource.Detected, 1440.0 / 1080);

        // Inconsistent with the standard ratios, so a re-check of every video file would clear it.
        var widescreen = await AddDetectedAsync("widescreen.mkv", AspectRatioSource.FromFile, 1920.0 / 1080);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(), await ReadStandardRatiosAsync((1.33, false))), CancellationToken);

        Assert.True(saved.IsSuccess);
        var converted = (await ReadVideoFileAsync(academy.VideoFile)).CurrentResult!;
        Assert.Equal((1.3333, AspectRatioSource.FromFile, Start), (converted.Result!.RawAspectRatio.Value, converted.Result!.AspectRatioSource, converted.StartedAt));
        var untouched = (await ReadVideoFileAsync(widescreen.VideoFile)).CurrentResult!;
        Assert.Equal(
            (1.7778, AspectRatioSource.FromFile, 0.9, 0, DetectedAt),
            (untouched.Result!.RawAspectRatio.Value, untouched.Result!.AspectRatioSource, untouched.Result!.Confidence, untouched.Result!.Samples.Count, untouched.StartedAt));
    }

    [Fact]
    public async Task Marking_a_ratio_check_picture_clears_a_from_file_result_that_snaps_to_it_and_returns_it_to_the_detection_queue()
    {
        _detector.Holds = true;
        var flat = await AddDetectedAsync("flat.mkv", AspectRatioSource.FromFile, 1.85);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(), await ReadStandardRatiosAsync((1.85, true))), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Null((await ReadVideoFileAsync(flat.VideoFile)).CurrentResult?.Id);
        Assert.Equal([(AspectRatioSource?)AspectRatioSource.FromFile], (await ReadDetectionsAsync(flat.VideoFile)).Select(detection => detection.Result?.AspectRatioSource));
        Assert.Equal([flat.VideoFile], await ReadDetectionQueueAsync());

        _detector.ReleaseAll();
        AdvanceToNextQueueCheck();
        await WaitUntilDetectedAsync(flat.VideoFile);
        Assert.Equal([flat.Path], _detector.Paths);
    }

    [Fact]
    public async Task Unmarking_a_check_picture_ratio_turns_a_detected_result_into_the_from_file_result_the_detector_writes()
    {
        _detector.Holds = true;
        var widescreen = await AddDetectedAsync("widescreen.mkv", AspectRatioSource.Detected, 1920.0 / 1080);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(), await ReadStandardRatiosAsync((1.78, false))), CancellationToken);

        Assert.True(saved.IsSuccess);
        var detections = await ReadDetectionsAsync(widescreen.VideoFile);
        Assert.Equal(2, detections.Count);
        var (converted, replaced) = (detections[0], detections[1]);
        Assert.Equal(converted.Id, (await ReadVideoFileAsync(widescreen.VideoFile)).CurrentResult?.Id);
        Assert.Equal(
            (DetectionOrigin.StandardRatiosChange, (string?)null, Start, TimeSpan.Zero),
            (converted.Origin, converted.Path?.Value, converted.StartedAt, converted.Duration));
        Assert.Equal(
            (1.7778, AspectRatioSource.FromFile, 0.9, 0),
            (converted.Result!.RawAspectRatio.Value, converted.Result!.AspectRatioSource, converted.Result!.Confidence, converted.Result!.Samples.Count));
        Assert.Equal(
            (1920.0 / 1080, 1920, 1080, "hevc", "bt709", 1, "8.1.2"),
            (converted.ContainerMetadata!.ContainerAspectRatio.Value, converted.ContainerMetadata.Width, converted.ContainerMetadata.Height, converted.ContainerMetadata.CodecName, converted.ContainerMetadata.ColorTransfer, converted.DetectorVersion, converted.FfmpegVersion));
        Assert.Equal((2.4, AspectRatioSource.Detected), (replaced.Result!.RawAspectRatio.Value, replaced.Result!.AspectRatioSource));
    }

    [Fact]
    public async Task A_re_check_keeps_overrides_and_clears_the_failure_of_each_result_it_clears_or_converts()
    {
        _detector.Holds = true;
        var flat = await AddDetectedAsync("flat.mkv", AspectRatioSource.FromFile, 1.85);
        var widescreen = await AddDetectedAsync("widescreen.mkv", AspectRatioSource.Detected, 1920.0 / 1080);
        var scope = await AddDetectedAsync("scope.mkv", AspectRatioSource.FromFile, 2.39);
        await SetOverrideAndFailureAsync(flat.VideoFile);
        await SetOverrideAndFailureAsync(widescreen.VideoFile);
        await SetOverrideAndFailureAsync(scope.VideoFile);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(), await ReadStandardRatiosAsync((1.78, false), (1.85, true))), CancellationToken);

        Assert.True(saved.IsSuccess);
        var cleared = await ReadVideoFileAsync(flat.VideoFile);
        var converted = await ReadVideoFileAsync(widescreen.VideoFile);
        var untouched = await ReadVideoFileAsync(scope.VideoFile);
        Assert.Equal(
            [(null, null, null), (AspectRatioSource.FromFile, null, null), (AspectRatioSource.FromFile, "timed out after 900s (3/12 samples)", DetectedAt)],
            new (AspectRatioSource?, string?, DateTimeOffset?)[]
            {
                (cleared.CurrentResult?.Result?.AspectRatioSource, cleared.LastFailure?.Error, cleared.LastFailure?.StartedAt),
                (converted.CurrentResult?.Result?.AspectRatioSource, converted.LastFailure?.Error, converted.LastFailure?.StartedAt),
                (untouched.CurrentResult?.Result?.AspectRatioSource, untouched.LastFailure?.Error, untouched.LastFailure?.StartedAt),
            });
        Assert.All(
            [cleared, converted, untouched],
            videoFile => Assert.Equal(
                (2.39, true, "Projected scope"),
                (videoFile.Override?.AspectRatio?.Value, videoFile.Override?.DontSend ?? false, videoFile.Override?.Note)));
        Assert.Equal([flat.VideoFile], await ReadDetectionQueueAsync());
    }

    [Fact]
    public async Task Clearing_every_check_picture_mark_turns_every_detected_result_into_a_from_file_one()
    {
        _detector.Holds = true;
        var academy = await AddDetectedAsync("academy.mkv", AspectRatioSource.Detected, 1440.0 / 1080);
        var widescreen = await AddDetectedAsync("widescreen.mkv", AspectRatioSource.Detected, 1920.0 / 1080);
        var between = await AddDetectedAsync("between.mkv", AspectRatioSource.Detected, 1.81);
        await StartAsync();

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(), await ReadStandardRatiosAsync((1.33, false), (1.78, false))), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            [(1.3333, AspectRatioSource.FromFile), (1.7778, AspectRatioSource.FromFile), (1.81, AspectRatioSource.FromFile)],
            [
                await ReadRawAndSourceAsync(academy.VideoFile),
                await ReadRawAndSourceAsync(widescreen.VideoFile),
                await ReadRawAndSourceAsync(between.VideoFile),
            ]);
    }

    [Fact]
    public async Task A_standard_ratios_change_and_its_re_check_commit_together()
    {
        _detector.Holds = true;
        var flat = await AddDetectedAsync("flat.mkv", AspectRatioSource.FromFile, 1.85);
        await StartAsync();
        var change = Change(await ReadSettingsAsync() with { SimultaneousDetections = 3 }, await ReadStandardRatiosAsync((1.85, true)));
        var mediaRows = TestVideoFile.MediaRowTableName(_host.Services.GetRequiredService<IDocumentStore>());
        await ExecuteAsync($"create trigger refuse_re_check before update on {mediaRows} begin select raise(abort, 'The re-check was refused.'); end");

        var refused = await _host.Runtime.SendCommandAsync(change, CancellationToken);

        Assert.StartsWith("Nothing was saved.", Assert.Single(refused.Errors).Message);
        Assert.Null(await StreamVersionAsync());
        Assert.Equal((2, false), await ReadSimultaneousDetectionsAndChecksPictureAsync(1.85));
        Assert.Equal((AspectRatioSource.FromFile, DetectedAt), await ReadSourceAndStartedAtAsync(flat.VideoFile));

        // A failure to append the settings' own events takes the re-check's writes back with them.
        await ExecuteAsync("drop trigger refuse_re_check");
        await ExecuteAsync($"create trigger refuse_settings before insert on fi_events when new.stream_id = '{DetectionSettings.StreamId}' begin select raise(abort, 'The settings were refused.'); end");

        refused = await _host.Runtime.SendCommandAsync(change, CancellationToken);

        Assert.StartsWith("Nothing was saved.", Assert.Single(refused.Errors).Message);
        Assert.Null(await StreamVersionAsync());
        Assert.Equal((AspectRatioSource.FromFile, DetectedAt), await ReadSourceAndStartedAtAsync(flat.VideoFile));

        await ExecuteAsync("drop trigger refuse_settings");
        var saved = await _host.Runtime.SendCommandAsync(change, CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal(2, await StreamVersionAsync());
        Assert.Equal((3, true), await ReadSimultaneousDetectionsAndChecksPictureAsync(1.85));
        Assert.Equal((null, null), await ReadSourceAndStartedAtAsync(flat.VideoFile));
    }

    [Fact]
    public async Task A_detection_running_during_a_re_check_is_cancelled_writes_nothing_and_runs_again_at_the_next_queue_check()
    {
        _detector.Holds = true;
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        // The detector is still held, so the save completes only by cancelling it.
        var saved = await _host.Runtime
            .SendCommandAsync(Change(await ReadSettingsAsync(), await ReadStandardRatiosAsync((1.85, true))), CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Empty(await ReadDetectionsAsync(alpha.VideoFile));
        AdvanceToNextQueueCheck();
        await Poll.UntilAsync(() => _detector.Paths.Count == 2);
        _detector.Release(alpha.Path);
        await WaitUntilDetectedAsync(alpha.VideoFile);
        Assert.Equal([alpha.Path, alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_save_from_a_stale_form_re_checks_against_the_settings_the_save_before_it_stored()
    {
        _detector.Holds = true;
        _detector.IgnoresCancellation = true;
        var blocker = await AddAsync("blocker.mkv");
        var between = await AddDetectedAsync("between.mkv", AspectRatioSource.Detected, 1.755);
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);
        var staleSettings = await ReadSettingsAsync();
        var staleStandardRatios = await ReadStandardRatiosAsync((1.85, true));

        // The first save waits on the blocker, which ignores its cancellation, and at 0.02 it would convert between.
        var first = _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(matchTolerance: 0.02), await ReadStandardRatiosAsync()), CancellationToken);
        await Poll.UntilAsync(() => _detector.Cancelled == 1);

        // The second save puts 0.04 back from a form loaded before the first save, and changes only a Check Picture flag.
        var second = _host.Runtime.SendCommandAsync(Change(staleSettings, staleStandardRatios), CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken);
        _detector.Release(blocker.Path);
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);

        // At the stored 0.04, between's container ratio snaps to 1.78, which is marked Check Picture, so its result from the file is cleared.
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(0.04, (await ReadSettingsAsync()).MatchTolerance);
        Assert.Equal((null, null), await ReadSourceAndStartedAtAsync(between.VideoFile));
    }

    [Fact]
    public async Task A_save_that_needs_no_re_check_leaves_a_running_detection_running()
    {
        _detector.Holds = true;
        var alpha = await AddAsync("alpha.mkv");
        await StartAsync();
        await Poll.UntilAsync(() => _detector.Running == 1);

        var settings = await ReadSettingsAsync() with { SimultaneousDetections = 3 };
        var saved = await _host.Runtime.SendCommandAsync(Change(settings, await ReadStandardRatiosAsync()), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal(3, (await ReadSettingsAsync()).SimultaneousDetections);
        Assert.Equal(1, _detector.Running);
        _detector.Release(alpha.Path);
        await WaitUntilDetectedAsync(alpha.VideoFile);
        Assert.Equal([alpha.Path], _detector.Paths);
    }

    [Fact]
    public async Task A_save_replaces_the_standard_ratios_and_announces_one_change_to_the_settings()
    {
        var changes = new ConcurrentQueue<ReadModelChanged>();
        using var subscription = _host.Services.GetRequiredService<ReadModelChangeListener>().ChangesTo(nameof(DetectionSettings)).Subscribe(changes.Enqueue);
        await StartAsync();
        var standardRatios = (await ReadStandardRatiosAsync()).Where(standardRatio => standardRatio.AspectRatio != 2.2).ToList();
        standardRatios.Add(new StandardRatio(2.76, true));

        var saved = await _host.Runtime.SendCommandAsync(Change(await ReadSettingsAsync(), standardRatios), CancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            [(1.33, true), (1.66, false), (1.78, true), (1.85, false), (2.0, false), (2.35, false), (2.39, false), (2.76, true)],
            (await ReadStandardRatiosAsync()).Select(standardRatio => (standardRatio.AspectRatio, standardRatio.ChecksPicture)).OrderBy(pair => pair.AspectRatio));
        Assert.Equal(new HashSet<string> { DetectionSettings.StreamId.ToString() }, Assert.Single(changes).Streams);
    }

    [Fact]
    public async Task Re_detect_all_clears_results_and_failures_keeps_overrides_and_detections_and_detects_again()
    {
        _detector.Holds = true;
        var flat = await AddDetectedAsync("flat.mkv", AspectRatioSource.FromFile, 1.85);
        await SetOverrideAndFailureAsync(flat.VideoFile);
        var failed = await AddAsync("failed.mkv");
        await SetFailureAsync(failed.VideoFile, "no crop detected in 12 samples");
        await StartAsync();

        var redetected = await _host.Runtime.SendCommandAsync(new RedetectAll(), CancellationToken);

        Assert.True(redetected.IsSuccess);
        var cleared = await ReadVideoFileAsync(flat.VideoFile);
        Assert.Equal((null, null), (cleared.CurrentResult?.Id, cleared.LastFailure?.Id));
        Assert.Equal(
            (2.39, true, "Projected scope"),
            (cleared.Override?.AspectRatio?.Value, cleared.Override?.DontSend ?? false, cleared.Override?.Note));
        var unfailed = await ReadVideoFileAsync(failed.VideoFile);
        Assert.Equal((null, null), (unfailed.CurrentResult?.Id, unfailed.LastFailure?.Id));
        Assert.Equal((2, 1), ((await ReadDetectionsAsync(flat.VideoFile)).Count, (await ReadDetectionsAsync(failed.VideoFile)).Count));
        Assert.Equal(new HashSet<FileHash> { flat.VideoFile, failed.VideoFile }, (await ReadDetectionQueueAsync()).ToHashSet());

        _detector.ReleaseAll();
        AdvanceToNextQueueCheck();
        await WaitUntilDetectedAsync(flat.VideoFile);
        await WaitUntilDetectedAsync(failed.VideoFile);
    }

    private Task StartAsync() => _orchestrator.StartAsync(CancellationToken);

    /// <summary>Moves the clock to the next queue check, which then runs.</summary>
    private void AdvanceToNextQueueCheck() => _timeProvider.Advance(DetectionOrchestrator.QueueCheckInterval);

    /// <summary>Writes a file named for its content and scans it, which adds a pending video file.</summary>
    private async Task<(string Path, FileHash VideoFile)> AddAsync(string name)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, WrittenAt);
        return (path, (await _scanner.ScanFileAsync(new LocalPath(path), CancellationToken))!.Value);
    }

    /// <summary>Adds a video file whose current result comes from a 1920x1080 container, recorded as the detector records it.</summary>
    private async Task<(string Path, FileHash VideoFile)> AddDetectedAsync(string name, AspectRatioSource source, double containerAspectRatio)
    {
        var added = await AddAsync(name);
        var result = source == AspectRatioSource.FromFile
            ? DetectionResult.FromFile(new AspectRatio(containerAspectRatio))
            : new DetectionResult(source, new AspectRatio(2.4), 1.0, [new CropSample(TimeSpan.FromSeconds(30), new CropBox(1920, 800))]);
        await AppendAsync(added.VideoFile, new AspectRatioDetected(added.VideoFile, CreateDetection(containerAspectRatio) with { Outcome = new DetectionOutcome.Succeeded(result) }));
        return added;
    }

    /// <summary>Records a failed detection of the video file as its last failure.</summary>
    private Task SetFailureAsync(FileHash videoFile, string error) =>
        AppendAsync(videoFile, new DetectionFailed(videoFile, CreateDetection(1920.0 / 1080) with { Outcome = new DetectionOutcome.Failed(error) }));

    private static Detection CreateDetection(double containerAspectRatio) => new(
        Guid.CreateVersion7(),
        DetectionOrigin.Queue,
        null,
        DetectedAt,
        TimeSpan.FromSeconds(40),
        1,
        "8.1.2",
        new ContainerMetadata(new AspectRatio(containerAspectRatio), 1920, 1080, "hevc", "bt709"),
        new DetectionOutcome.Failed("not detected yet"));

    private async Task SetOverrideAndFailureAsync(FileHash videoFile)
    {
        await AppendAsync(videoFile, new OverrideSaved(new Override(new AspectRatio(2.39), true, "Projected scope", DetectedAt)));
        await SetFailureAsync(videoFile, "timed out after 900s (3/12 samples)");
    }

    private Task AppendAsync(FileHash videoFile, params object[] events) =>
        TestVideoFile.AppendAsync(_host.Services.GetRequiredService<IDocumentStore>(), videoFile, events, CancellationToken);

    private static ChangeDetectionSettings Change(ChangeDetectionSettings settings, IReadOnlyList<StandardRatio> standardRatios) =>
        settings with { StandardRatios = standardRatios };

    /// <summary>The stored detection settings as the page sends them, with the tolerance replaced when one is given.</summary>
    private async Task<ChangeDetectionSettings> ReadSettingsAsync(double? matchTolerance = null)
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        var settings = await DetectionSettings.ReadAsync(session, CancellationToken);
        return new ChangeDetectionSettings(
            settings.SimultaneousDetections,
            settings.PictureMeasurement.SampleCount,
            settings.PictureMeasurement.SkipStartAndEndPercent,
            settings.TimeoutSeconds,
            settings.PictureMeasurement.BlackLevelSdr,
            settings.PictureMeasurement.BlackLevelHdr,
            settings.StandardRatios.Ratios,
            matchTolerance ?? settings.StandardRatios.MatchTolerance);
    }

    /// <summary>The stored standard ratios, with the Check Picture flag of each given ratio replaced.</summary>
    private async Task<List<StandardRatio>> ReadStandardRatiosAsync(params (double AspectRatio, bool ChecksPicture)[] flags)
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        var standardRatios = (await DetectionSettings.ReadAsync(session, CancellationToken)).StandardRatios.Ratios;
        return [.. standardRatios.Select(standardRatio => flags.FirstOrDefault(flag => flag.AspectRatio == standardRatio.AspectRatio) is { AspectRatio: > 0 } flag
            ? standardRatio with { ChecksPicture = flag.ChecksPicture }
            : standardRatio)];
    }

    private async Task<(int, bool)> ReadSimultaneousDetectionsAndChecksPictureAsync(double aspectRatio)
    {
        var settings = await ReadSettingsAsync();
        return (settings.SimultaneousDetections, settings.StandardRatios.Single(standardRatio => standardRatio.AspectRatio == aspectRatio).ChecksPicture);
    }

    /// <summary>The detection settings' stream version; null when the stream has no events.</summary>
    private async Task<long?> StreamVersionAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        return (await session.Events.FetchStreamStateAsync(DetectionSettings.StreamId, CancellationToken))?.Version;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection(_host.UnpooledConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    /// <summary>The video files in the detection queue, newest first.</summary>
    private async Task<IEnumerable<FileHash>> ReadDetectionQueueAsync()
    {
        await using var session = _host.Services.GetRequiredService<IDocumentStore>().QuerySession();
        return (await session.ReadDetectionQueueAsync(int.MaxValue, [], CancellationToken)).Select(request => request.VideoFile);
    }

    private async Task<VideoFile> ReadVideoFileAsync(FileHash videoFile) =>
        (await TestVideoFile.ReadVideoFileAsync(_host.Services.GetRequiredService<IDocumentStore>(), videoFile, CancellationToken))!;

    /// <summary>The video file's detections, newest first.</summary>
    private Task<IReadOnlyList<Detection>> ReadDetectionsAsync(FileHash videoFile) =>
        TestVideoFile.ReadDetectionsAsync(_host.Services.GetRequiredService<IDocumentStore>(), videoFile, CancellationToken);

    /// <summary>The source and start of the current result.</summary>
    private async Task<(AspectRatioSource?, DateTimeOffset?)> ReadSourceAndStartedAtAsync(FileHash videoFile)
    {
        var detectionResult = (await ReadVideoFileAsync(videoFile)).CurrentResult;
        return (detectionResult?.Result?.AspectRatioSource, detectionResult?.StartedAt);
    }

    private async Task<(double?, AspectRatioSource?)> ReadRawAndSourceAsync(FileHash videoFile)
    {
        var detectionResult = (await ReadVideoFileAsync(videoFile)).CurrentResult;
        return (detectionResult?.Result?.RawAspectRatio.Value, detectionResult?.Result?.AspectRatioSource);
    }

    private Task WaitUntilDetectedAsync(FileHash videoFile) =>
        Poll.UntilAsync(async () => (await ReadVideoFileAsync(videoFile)).CurrentResult?.Id is not null);
}
