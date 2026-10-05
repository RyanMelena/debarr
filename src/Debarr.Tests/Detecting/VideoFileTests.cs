using Debarr.Detecting;
using Debarr.Scanning;
using FluentResults;

namespace Debarr.Tests.Detecting;

public sealed class VideoFileTests
{
    private static readonly FileHash Hash = TestFileHash.For("arrival");

    private static readonly LocalPath Film = new("/media/Arrival.mkv");

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, TimeSpan.Zero);

    private static readonly ContainerMetadata Widescreen = new(new AspectRatio(1.78), 1920, 1080, "hevc", null);

    private static readonly FileStat Stat = new(42, Now.AddDays(-1));

    private static readonly VideoFile Discovered = VideoFile.Create(new VideoFileDiscovered(Hash, 42, Now));

    [Fact]
    public void A_first_seen_hash_is_discovered_with_its_size_and_its_path()
    {
        var events = AddFilePathHandler.Handle(new AddFilePath(Hash, Film, Stat, Now), null);

        Assert.Equal([new VideoFileDiscovered(Hash, 42, Now), new FilePathAdded(Hash, Film, Stat, Now, Now)], events);
    }

    [Fact]
    public void A_new_path_gets_a_new_first_seen_time_and_a_rehashed_path_keeps_its_own()
    {
        var videoFile = Discovered.Apply(new FilePathAdded(Hash, Film, Stat, Now, Now));
        var copy = new LocalPath("/backup/Arrival.mkv");
        var touched = new FileStat(42, Now);

        Assert.Equal(
            [new FilePathAdded(Hash, copy, Stat, Now.AddDays(1), Now.AddDays(1))],
            AddFilePathHandler.Handle(new AddFilePath(Hash, copy, Stat, Now.AddDays(1)), videoFile));
        Assert.Equal(
            [new FilePathAdded(Hash, Film, touched, Now, Now.AddDays(1))],
            AddFilePathHandler.Handle(new AddFilePath(Hash, Film, touched, Now.AddDays(1)), videoFile));
        Assert.Equal([new FilePath(Film, touched, Now, Now.AddDays(1))], videoFile.Apply(new FilePathAdded(Hash, Film, touched, Now, Now.AddDays(1))).FilePaths);
    }

    [Fact]
    public void File_paths_are_held_most_recently_hashed_first_then_by_path()
    {
        var backup = new LocalPath("/backup/Arrival.mkv");
        var copy = new LocalPath("/copy/Arrival.mkv");

        var videoFile = Discovered
            .Apply(new FilePathAdded(Hash, Film, Stat, Now, Now))
            .Apply(new FilePathAdded(Hash, copy, Stat, Now, Now))
            .Apply(new FilePathAdded(Hash, backup, Stat, Now.AddDays(1), Now.AddDays(1)));

        Assert.Equal([backup, copy, Film], videoFile.FilePaths.Select(filePath => filePath.Path));
        Assert.Equal(new DetectionRequest(Hash, backup, DetectionOrigin.DetectNow), videoFile.ToDetectionRequest(DetectionOrigin.DetectNow));
        Assert.Equal([Film, backup, copy], videoFile.Apply(new FilePathAdded(Hash, Film, Stat, Now, Now.AddDays(2))).FilePaths.Select(filePath => filePath.Path));
        Assert.Null(Discovered.ToDetectionRequest(DetectionOrigin.DetectNow));
    }

    [Fact]
    public void A_path_hashed_again_unchanged_appends_nothing()
    {
        var videoFile = Discovered.Apply(new FilePathAdded(Hash, Film, Stat, Now, Now));

        Assert.Empty(AddFilePathHandler.Handle(new AddFilePath(Hash, Film, new FileStat(42, Now.AddDays(-1)), Now.AddDays(1)), videoFile));
    }

    [Fact]
    public void A_path_hashed_again_with_a_changed_size_or_a_changed_modification_time_records_the_new_stat()
    {
        var videoFile = Discovered.Apply(new FilePathAdded(Hash, Film, Stat, Now, Now));
        var resized = new FileStat(43, Stat.ModifiedAt);
        var touched = new FileStat(42, Stat.ModifiedAt.AddMilliseconds(1));

        Assert.Equal(
            [new FilePathAdded(Hash, Film, resized, Now, Now.AddDays(1))],
            AddFilePathHandler.Handle(new AddFilePath(Hash, Film, resized, Now.AddDays(1)), videoFile));
        Assert.Equal(
            [new FilePathAdded(Hash, Film, touched, Now, Now.AddDays(1))],
            AddFilePathHandler.Handle(new AddFilePath(Hash, Film, touched, Now.AddDays(1)), videoFile));
    }

    [Fact]
    public void A_path_the_video_file_has_is_removed_once()
    {
        var videoFile = Discovered.Apply(new FilePathAdded(Hash, Film, Stat, Now, Now));

        Assert.Equal([new FilePathRemoved(Hash, Film, Now)], RemoveFilePathHandler.Handle(new RemoveFilePath(Hash, Film, Now), videoFile));
        Assert.Empty(videoFile.Apply(new FilePathRemoved(Hash, Film, Now)).FilePaths);
        Assert.Empty(RemoveFilePathHandler.Handle(new RemoveFilePath(Hash, Film, Now), Discovered));
        Assert.Empty(RemoveFilePathHandler.Handle(new RemoveFilePath(Hash, Film, Now), null));
    }

    [Fact]
    public void A_detection_result_becomes_the_current_result_and_clears_the_last_failure()
    {
        var failed = Recorded(Discovered, Result.Fail("ffprobe failed"));

        var detected = Recorded(failed, Result.Ok(DetectionResult.FromFile(new AspectRatio(2.39))));

        Assert.Equal(VideoFileStatus.FromFile, detected.Status);
        Assert.Null(detected.LastFailure);
        Assert.Equal(2, detected.Detections.Count);
    }

    [Fact]
    public void A_failure_is_recorded_beside_the_current_result_and_only_a_result_replaces_it()
    {
        var detected = Recorded(Discovered, Result.Ok(DetectionResult.FromFile(new AspectRatio(2.39))));

        var failed = Recorded(detected, Result.Fail("timed out after 900s (3/12 samples)"));

        Assert.Equal(detected.CurrentResult, failed.CurrentResult);
        Assert.Equal(new DetectionOutcome.Failed("timed out after 900s (3/12 samples)"), failed.LastFailure!.Outcome);
        Assert.Equal(VideoFileStatus.FromFile, failed.Status);
    }

    [Fact]
    public void A_detection_records_what_started_it_the_path_the_versions_and_the_metadata()
    {
        var detectionId = Guid.CreateVersion7();
        var recorded = Assert.IsType<DetectionFailed>(Assert.Single(RecordDetectionHandler.Handle(
            new RecordDetection(detectionId, Hash, DetectionOrigin.DetectNow, Film, Now, TimeSpan.FromSeconds(9), 3, new DetectorOutcome("7.1", Widescreen, Result.Fail(["no picture", "no duration"]))),
            Discovered)));

        Assert.Equal(
            new DetectionFailed(Hash, new Detection(detectionId, DetectionOrigin.DetectNow, Film, Now, TimeSpan.FromSeconds(9), 3, "7.1", Widescreen, new DetectionOutcome.Failed("no picture; no duration"))),
            recorded);
    }

    [Fact]
    public void A_detection_of_a_missing_or_archived_video_file_records_nothing()
    {
        var command = new RecordDetection(
            Guid.CreateVersion7(), Hash, DetectionOrigin.Queue, Film, Now, TimeSpan.FromSeconds(9), 3, new DetectorOutcome("7.1", Widescreen, Result.Ok(DetectionResult.FromFile(new AspectRatio(1.78)))));

        Assert.Empty(RecordDetectionHandler.Handle(command, null));
        Assert.Empty(RecordDetectionHandler.Handle(command, Discovered.Apply(new VideoFileArchived(Now))));
        Assert.Single(RecordDetectionHandler.Handle(command, Discovered.Apply(new VideoFileArchived(Now)).Apply(new VideoFileRestored(Hash, 42, Now, null, null, null, Now))));
    }

    [Fact]
    public void An_archived_video_file_found_again_is_restored_with_its_state_and_gets_the_path_as_a_new_one()
    {
        var result = new Detection(Guid.CreateVersion7(), DetectionOrigin.Queue, Film, Now, TimeSpan.FromSeconds(9), 3, "7.1", Widescreen, new DetectionOutcome.Succeeded(DetectionResult.FromFile(new AspectRatio(1.78))));
        var failure = result with { Id = Guid.CreateVersion7(), Outcome = new DetectionOutcome.Failed("no picture") };
        var @override = new Override(new AspectRatio(2.39), false, "Kept", Now);
        var archived = Discovered
            .Apply(new FilePathAdded(Hash, Film, Stat, Now, Now))
            .Apply(new AspectRatioDetected(Hash, result))
            .Apply(new DetectionFailed(Hash, failure))
            .Apply(new OverrideSaved(@override))
            .Apply(new FilePathRemoved(Hash, Film, Now.AddDays(1)))
            .Apply(new VideoFileArchived(Now.AddDays(2)));

        var events = AddFilePathHandler.Handle(new AddFilePath(Hash, Film, Stat, Now.AddDays(3)), archived);

        Assert.Equal(
            [new VideoFileRestored(Hash, 42, Now, result, failure, @override, Now.AddDays(3)), new FilePathAdded(Hash, Film, Stat, Now.AddDays(3), Now.AddDays(3))],
            events);
        Assert.False(archived.Apply((VideoFileRestored)events[0]).Archived);
    }

    [Theory]
    [InlineData(null, false, "Detected", false, VideoFileStatus.Detected)]
    [InlineData(null, false, "FromFile", false, VideoFileStatus.FromFile)]
    [InlineData(null, false, null, true, VideoFileStatus.Failed)]
    [InlineData(null, false, null, false, VideoFileStatus.Pending)]
    [InlineData(2.39, false, "Detected", true, VideoFileStatus.Manual)]
    [InlineData(null, true, null, false, VideoFileStatus.Manual)]
    public void The_status_follows_the_override_then_the_current_result_then_the_last_failure(
        double? overrideAspectRatio,
        bool dontSend,
        string? source,
        bool failed,
        VideoFileStatus expected)
    {
        var videoFile = Discovered;
        if (source is not null)
        {
            videoFile = Recorded(videoFile, Result.Ok(new DetectionResult(Enum.Parse<AspectRatioSource>(source), new AspectRatio(2.39), 1, [])));
        }

        if (failed)
        {
            videoFile = Recorded(videoFile, Result.Fail("ffprobe failed"));
        }

        videoFile = videoFile.Apply(new OverrideSaved(Override.Create(overrideAspectRatio, dontSend, null, Now).Value));

        Assert.Equal(expected, videoFile.Status);
    }

    [Fact]
    public void The_stream_id_is_the_first_half_of_the_hash()
    {
        Assert.Equal(Hash.Value[..32], Hash.StreamId.ToString("N"));
        Assert.Equal(Hash.StreamId, new AddFilePath(Hash, Film, Stat, Now).VideoFileId);
    }

    /// <summary>The video file after a detection from the queue records the result.</summary>
    private static VideoFile Recorded(VideoFile videoFile, Result<DetectionResult> result, ContainerMetadata? containerMetadata = null) =>
        Assert.Single(RecordDetectionHandler.Handle(
            new RecordDetection(Guid.CreateVersion7(), Hash, DetectionOrigin.Queue, Film, Now, TimeSpan.FromSeconds(9), 3, new DetectorOutcome("7.1", containerMetadata, result)),
            videoFile)) switch
        {
            AspectRatioDetected detected => videoFile.Apply(detected),
            DetectionFailed failed => videoFile.Apply(failed),
            var other => throw new InvalidOperationException($"{other.GetType().Name} is not a recorded detection."),
        };
}
