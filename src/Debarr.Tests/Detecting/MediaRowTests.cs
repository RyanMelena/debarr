using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using FluentResults;
using Wolverine.Runtime;

namespace Debarr.Tests.Detecting;

public sealed class MediaRowTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    /// <summary>
    /// Every event a video file's stream holds folds into its Media row, so the row's version follows its stream's,
    /// which the commands that append at the row's version rely on.
    /// </summary>
    [Fact]
    public void Media_rows_fold_every_event_a_video_file_applies()
    {
        static IEnumerable<Type> EventTypes(Type type, params string[] methods) =>
            type.GetMethods().Where(method => methods.Contains(method.Name)).Select(method => method.GetParameters()[0].ParameterType);

        Assert.Equal(
            EventTypes(typeof(VideoFile), nameof(VideoFile.Create), nameof(VideoFile.Apply)).OrderBy(type => type.Name),
            EventTypes(typeof(MediaRowProjection), nameof(MediaRowProjection.Create), nameof(MediaRowProjection.Apply), nameof(MediaRowProjection.ShouldDelete)).OrderBy(type => type.Name));
    }

    [Fact]
    public async Task The_status_follows_the_override_then_the_current_results_source_then_the_last_failure()
    {
        var pending = await AddAsync("pending");
        var failed = await AddAsync("failed", new DetectionFailed(TestFileHash.For("failed"), TestVideoFile.Failed("ffprobe failed")));
        var fromFile = await AddAsync("from-file", new AspectRatioDetected(TestFileHash.For("from-file"), TestVideoFile.Detected(1.78, AspectRatioSource.FromFile)));
        var detected = await AddAsync(
            "detected",
            new AspectRatioDetected(TestFileHash.For("detected"), TestVideoFile.Detected(2.39)),
            new DetectionFailed(TestFileHash.For("detected"), TestVideoFile.Failed("Detect Now failed")));
        var ratio = await AddAsync(
            "ratio",
            new AspectRatioDetected(TestFileHash.For("ratio"), TestVideoFile.Detected(2.39)),
            new OverrideSaved(new Override(new AspectRatio(2.4), false, null, Now)));
        var dontSend = await AddAsync(
            "dont-send",
            new DetectionFailed(TestFileHash.For("dont-send"), TestVideoFile.Failed("ffprobe failed")),
            new OverrideSaved(new Override(null, true, "Trailer", Now)));
        var cleared = await AddAsync(
            "cleared",
            new AspectRatioDetected(TestFileHash.For("cleared"), TestVideoFile.Detected(2.39)),
            new OverrideSaved(new Override(null, false, "Kept", Now)),
            new DetectionResultCleared(Now));

        Assert.Equal(VideoFileStatus.Pending, (await LoadAsync(pending)).Status);
        Assert.Equal(VideoFileStatus.Failed, (await LoadAsync(failed)).Status);
        Assert.Equal(VideoFileStatus.FromFile, (await LoadAsync(fromFile)).Status);
        Assert.Equal(VideoFileStatus.Detected, (await LoadAsync(detected)).Status);
        Assert.Equal(VideoFileStatus.Manual, (await LoadAsync(ratio)).Status);
        Assert.Equal(VideoFileStatus.Manual, (await LoadAsync(dontSend)).Status);
        Assert.Equal(VideoFileStatus.Pending, (await LoadAsync(cleared)).Status);
    }

    [Fact]
    public async Task The_row_holds_the_size_the_current_result_the_last_failure_and_the_override()
    {
        var result = TestVideoFile.Detected(2.39, containerAspectRatio: 1.78, detectorVersion: 2, confidence: 0.75, ffmpegVersion: "7.1");
        var failure = TestVideoFile.Failed("Detect Now failed");
        var videoFile = await AddAsync(
            "film",
            new AspectRatioDetected(TestFileHash.For("film"), result),
            new DetectionFailed(TestFileHash.For("film"), failure),
            new OverrideSaved(new Override(new AspectRatio(2.2), true, "Director's cut", Now)));

        var row = await LoadAsync(videoFile);

        Assert.Equal((videoFile.StreamId, videoFile, 1L, Now), (row.Id, row.FileHash, row.Size, row.FirstSeenAt));
        Assert.Equal(
            new MediaRowResult(AspectRatioSource.Detected, new AspectRatio(2.39), 0.75, result.ContainerMetadata, 2, "7.1"),
            row.CurrentResult);
        Assert.Equal(new MediaRowFailure("Detect Now failed"), row.LastFailure);
        Assert.Equal(new Override(new AspectRatio(2.2), true, "Director's cut", Now), row.Override);
    }

    [Fact]
    public async Task A_new_result_clears_the_last_failure_and_a_converted_result_replaces_the_current_one()
    {
        var detected = TestVideoFile.Detected(2.39, containerAspectRatio: 1.78);
        var converted = TestVideoFile.Detected(1.78, AspectRatioSource.FromFile, path: null, containerAspectRatio: 1.78, origin: DetectionOrigin.StandardRatiosChange);
        var videoFile = await AddAsync(
            "film",
            new DetectionFailed(TestFileHash.For("film"), TestVideoFile.Failed("ffprobe failed")),
            new AspectRatioDetected(TestFileHash.For("film"), detected));

        var row = await LoadAsync(videoFile);
        Assert.Equal((MediaRowResult.From(detected), null, VideoFileStatus.Detected), (row.CurrentResult, row.LastFailure, row.Status));

        await TestVideoFile.AppendAsync(Store, videoFile, [new DetectionFailed(videoFile, TestVideoFile.Failed("Detect Now failed")), new DetectionResultConverted(videoFile, converted)], CancellationToken);

        row = await LoadAsync(videoFile);
        Assert.Equal((MediaRowResult.From(converted), AspectRatioSource.FromFile, null, VideoFileStatus.FromFile), (row.CurrentResult, row.CurrentResult?.Source, row.LastFailure, row.Status));
    }

    [Fact]
    public async Task Clearing_removes_the_current_result_and_the_last_failure_and_keeps_the_override()
    {
        var @override = new Override(null, false, "Kept", Now);
        var videoFile = await AddAsync(
            "film",
            new AspectRatioDetected(TestFileHash.For("film"), TestVideoFile.Detected(2.39)),
            new DetectionFailed(TestFileHash.For("film"), TestVideoFile.Failed("Detect Now failed")),
            new OverrideSaved(@override),
            new DetectionResultCleared(Now));

        var row = await LoadAsync(videoFile);

        Assert.Equal((null, null, @override), (row.CurrentResult, row.LastFailure, row.Override));
    }

    [Fact]
    public async Task File_paths_are_held_most_recently_hashed_first_and_a_rehashed_path_replaces_its_entry()
    {
        var videoFile = await TestVideoFile.AddAsync(Store, ["/b/film.mkv", "/a/film.mkv"], CancellationToken, hashedAt: Now);
        var copied = new FileStat(1, Now.AddDays(-2));
        var touched = new FileStat(1, Now.AddDays(-1));
        await TestVideoFile.AppendAsync(
            Store,
            videoFile,
            [
                new FilePathAdded(videoFile, new LocalPath("/c/film.mkv"), copied, Now.AddMinutes(1), Now.AddMinutes(1)),
                new FilePathAdded(videoFile, new LocalPath("/b/film.mkv"), touched, Now, Now.AddMinutes(2)),
            ],
            CancellationToken);

        var row = await LoadAsync(videoFile);

        Assert.Equal(
            [
                new FilePath(new LocalPath("/b/film.mkv"), touched, Now, Now.AddMinutes(2)),
                new FilePath(new LocalPath("/c/film.mkv"), copied, Now.AddMinutes(1), Now.AddMinutes(1)),
                new FilePath(new LocalPath("/a/film.mkv"), TestVideoFile.Stat, Now, Now),
            ],
            row.FilePaths);
    }

    [Fact]
    public async Task A_removed_path_leaves_the_row_and_records_when_the_video_file_last_lost_a_path()
    {
        var videoFile = await AddAsync("film");
        Assert.Null((await LoadAsync(videoFile)).LastPathRemovedAt);

        await TestVideoFile.AppendAsync(Store, videoFile, [new FilePathRemoved(videoFile, new LocalPath("film"), Now.AddDays(1))], CancellationToken);

        var row = await LoadAsync(videoFile);
        Assert.Equal((false, Now.AddDays(1)), (row.HasFilePath, row.LastPathRemovedAt));
        Assert.Empty(row.FilePaths);
    }

    [Fact]
    public async Task The_path_key_is_the_smallest_path_ignoring_case_with_ties_broken_by_the_path()
    {
        var videoFile = await TestVideoFile.AddAsync(Store, ["/Media/b.mkv", "/media/B.mkv", "/media/a.MKV"], CancellationToken);

        var row = await LoadAsync(videoFile);

        Assert.Equal("/media/a.mkv\n/media/a.MKV", row.PathKey);
        Assert.Equal(["/media/a.MKV", "/Media/b.mkv", "/media/B.mkv"], row.FilePathsByPath.Select(filePath => filePath.Path.Value));
        Assert.Equal(["/media/b.mkv", "/media/b.mkv", "/media/a.mkv"], row.PathsText.Split('\n').Order(StringComparer.Ordinal).Reverse());
    }

    [Fact]
    public async Task A_video_file_is_in_the_detection_queue_while_pending_with_a_file_path()
    {
        var pending = await AddAsync("pending");
        var overridden = await AddAsync("overridden", new OverrideSaved(new Override(null, true, null, Now)));
        var detected = await AddAsync("detected", new AspectRatioDetected(TestFileHash.For("detected"), TestVideoFile.Detected(2.39)));
        var failed = await AddAsync("failed", new DetectionFailed(TestFileHash.For("failed"), TestVideoFile.Failed("ffprobe failed")));
        var noPath = await AddAsync("no-path", new FilePathRemoved(TestFileHash.For("no-path"), new LocalPath("no-path"), Now));

        Assert.True((await LoadAsync(pending)).InDetectionQueue);
        Assert.True((await LoadAsync(overridden)).InDetectionQueue);
        Assert.False((await LoadAsync(detected)).InDetectionQueue);
        Assert.False((await LoadAsync(failed)).InDetectionQueue);
        Assert.False((await LoadAsync(noPath)).InDetectionQueue);
    }

    [Fact]
    public async Task A_rebuild_replays_each_video_file()
    {
        var result = TestVideoFile.Detected(2.39);
        var videoFile = await AddAsync(
            "film",
            new AspectRatioDetected(TestFileHash.For("film"), result),
            new OverrideSaved(new Override(new AspectRatio(2.2), false, null, Now)));
        await using (var session = Store.LightweightSession())
        {
            var row = await session.LoadAsync<MediaRow>(videoFile.StreamId, CancellationToken);
            row!.CurrentResult = null;
            row.Override = null;
            session.Store(row);
            await session.SaveChangesAsync(CancellationToken);
        }

        await RebuildAsync(MediaRowProjection.ReadModel);

        var rebuilt = await LoadAsync(videoFile);
        Assert.Equal((MediaRowResult.From(result), 2.2, VideoFileStatus.Manual), (rebuilt.CurrentResult, rebuilt.Override?.AspectRatio?.Value, rebuilt.Status));
    }

    [Fact]
    public async Task An_archived_video_file_has_no_row_and_a_rebuild_leaves_it_out()
    {
        var videoFile = await AddAsync("film", new FilePathRemoved(TestFileHash.For("film"), new LocalPath("film"), Now));

        var archived = await SendAsync(new ArchiveVideoFiles([videoFile], Now.AddDays(1)));

        Assert.True(archived.IsSuccess);
        Assert.Null(await LoadOrNullAsync(videoFile));
        await RebuildAsync(MediaRowProjection.ReadModel);
        Assert.Null(await LoadOrNullAsync(videoFile));
    }

    [Fact]
    public async Task A_restored_video_file_has_its_row_back_with_its_state_and_a_rebuild_keeps_it()
    {
        var result = TestVideoFile.Detected(2.39);
        var failure = TestVideoFile.Failed("Detect Now failed");
        var @override = new Override(new AspectRatio(2.2), false, "Director's cut", Now);
        var videoFile = await AddAsync(
            "film",
            new AspectRatioDetected(TestFileHash.For("film"), result),
            new DetectionFailed(TestFileHash.For("film"), failure),
            new OverrideSaved(@override),
            new FilePathRemoved(TestFileHash.For("film"), new LocalPath("film"), Now));
        Assert.True((await SendAsync(new ArchiveVideoFiles([videoFile], Now.AddDays(1)))).IsSuccess);

        var returned = new LocalPath("/backup/film.mkv");
        Assert.True((await SendAsync(new UnarchiveVideoFile(videoFile))).IsSuccess);
        Assert.True((await SendAsync(new AddFilePath(videoFile, returned, TestVideoFile.Stat, Now.AddDays(2)))).IsSuccess);
        var restored = await LoadAsync(videoFile);
        await RebuildAsync(MediaRowProjection.ReadModel);
        var rebuilt = await LoadAsync(videoFile);

        Assert.All(
            [restored, rebuilt],
            row =>
            {
                Assert.Equal(
                    (Now, MediaRowResult.From(result), MediaRowFailure.From(failure), @override, VideoFileStatus.Manual),
                    (row.FirstSeenAt, row.CurrentResult, row.LastFailure, row.Override, row.Status));
                Assert.Equal([new FilePath(returned, TestVideoFile.Stat, Now.AddDays(2), Now.AddDays(2))], row.FilePaths);
            });
    }

    [Fact]
    public async Task A_commit_to_a_video_file_announces_a_change_to_the_media_rows()
    {
        List<ReadModelChanged> changes = [];
        using var feed = GetAppService<ActivityFeed>().Events.Subscribe(activity =>
        {
            if (activity is ReadModelChanged changed)
            {
                lock (changes)
                {
                    changes.Add(changed);
                }
            }
        });

        var videoFile = await TestVideoFile.AddAsync(Store, "/media/film.mkv", CancellationToken);

        await Poll.UntilAsync(() =>
        {
            lock (changes)
            {
                return changes.Any(changed => changed.ReadModel == MediaRowProjection.ReadModel && changed.Streams.Contains(videoFile.StreamId.ToString()));
            }
        });
    }

    /// <summary>Discovers a video file at <paramref name="path"/>, with the hash <see cref="TestFileHash.For"/> gives it, and appends <paramref name="events"/> to it.</summary>
    private async Task<FileHash> AddAsync(string path, params object[] events)
    {
        var videoFile = await TestVideoFile.AddAsync(Store, path, CancellationToken, hashedAt: Now);
        if (events.Length > 0)
        {
            await TestVideoFile.AppendAsync(Store, videoFile, events, CancellationToken);
        }

        return videoFile;
    }

    private async Task<Result> SendAsync(object command) =>
        await GetAppService<IWolverineRuntime>().SendCommandAsync(command, CancellationToken);

    private async Task<MediaRow> LoadAsync(FileHash videoFile) =>
        await LoadOrNullAsync(videoFile) ?? throw new InvalidOperationException($"No Media row for {videoFile}.");

    private async Task<MediaRow?> LoadOrNullAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return await session.LoadAsync<MediaRow>(videoFile.StreamId, CancellationToken);
    }
}
