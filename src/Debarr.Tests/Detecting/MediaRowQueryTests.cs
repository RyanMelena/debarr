using Debarr.Detecting;
using Debarr.Scanning;
using Fisher.Linq;

namespace Debarr.Tests.Detecting;

public sealed class MediaRowQueryTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    public static TheoryData<VideoFileStatus?, MediaRowSort, bool> Views()
    {
        var views = new TheoryData<VideoFileStatus?, MediaRowSort, bool>();
        foreach (var status in new VideoFileStatus?[] { null, VideoFileStatus.Failed })
        {
            foreach (var sort in Enum.GetValues<MediaRowSort>())
            {
                views.Add(status, sort, false);
                views.Add(status, sort, true);
            }
        }

        return views;
    }

    [Fact]
    public async Task Each_status_filter_lists_its_video_files_once_whatever_their_path_count()
    {
        var detected = await AddAsync(["/media/detected.mkv", "/backup/detected.mkv"], new AspectRatioDetected(TestFileHash.For("/media/detected.mkv"), TestVideoFile.Detected(2.39)));
        var failed = await AddAsync(["/media/failed.mkv"], new DetectionFailed(TestFileHash.For("/media/failed.mkv"), TestVideoFile.Failed("ffprobe failed")));
        var pending = await AddAsync(["/media/pending.mkv", "/backup/pending.mkv", "/other/pending.mkv"]);

        Assert.Equal([detected], await ReadFileHashesAsync(new MediaRowView(VideoFileStatus.Detected, null, MediaRowSort.Path, false)));
        Assert.Equal([failed], await ReadFileHashesAsync(new MediaRowView(VideoFileStatus.Failed, null, MediaRowSort.Path, false)));
        Assert.Equal([pending], await ReadFileHashesAsync(new MediaRowView(VideoFileStatus.Pending, null, MediaRowSort.Path, false)));
        Assert.Empty(await ReadFileHashesAsync(new MediaRowView(VideoFileStatus.Manual, null, MediaRowSort.Path, false)));
        Assert.Equal([detected, pending, failed], await ReadFileHashesAsync(new MediaRowView(null, null, MediaRowSort.Path, false)));
    }

    [Theory]
    [InlineData(MediaRowSort.Path, new[] { "a", "B", "c", "D" })]
    [InlineData(MediaRowSort.AspectRatio, new[] { "D", "a", "c", "B" })]
    [InlineData(MediaRowSort.Confidence, new[] { "D", "B", "c", "a" })]
    [InlineData(MediaRowSort.Status, new[] { "D", "a", "B", "c" })]
    [InlineData(MediaRowSort.FirstSeen, new[] { "B", "c", "D", "a" })]
    public async Task Each_sort_orders_both_ways_and_breaks_ties_by_path_in_the_same_direction(MediaRowSort sort, string[] ascending)
    {
        await AddAsync(["/media/a.mkv"], Now.AddMinutes(2), new AspectRatioDetected(TestFileHash.For("/media/a.mkv"), TestVideoFile.Detected(1.78, confidence: 0.9)));
        await AddAsync(["/media/B.mkv"], Now, new AspectRatioDetected(TestFileHash.For("/media/B.mkv"), TestVideoFile.Detected(2.39, confidence: 0.5)), new OverrideSaved(new Override(null, false, "Note", Now)));
        await AddAsync(["/media/c.mkv"], Now, new AspectRatioDetected(TestFileHash.For("/media/c.mkv"), TestVideoFile.Detected(1.78, confidence: 0.75)));
        await AddAsync(["/media/D.mkv"], Now, new DetectionFailed(TestFileHash.For("/media/D.mkv"), TestVideoFile.Failed("ffprobe failed")));

        var names = await ReadNamesAsync(new MediaRowView(null, null, sort, false));
        var descendingNames = await ReadNamesAsync(new MediaRowView(null, null, sort, true));

        Assert.Equal(ascending, names);
        Assert.Equal(ascending.Reverse(), descendingNames);
    }

    [Fact]
    public async Task The_ratio_sort_puts_dont_send_with_no_ratio_and_an_override_in_place_of_its_raw_ratio()
    {
        await AddAsync(["/media/raw.mkv"], new AspectRatioDetected(TestFileHash.For("/media/raw.mkv"), TestVideoFile.Detected(1.85)));
        await AddAsync(
            ["/media/override.mkv"],
            new AspectRatioDetected(TestFileHash.For("/media/override.mkv"), TestVideoFile.Detected(2.39)),
            new OverrideSaved(new Override(new AspectRatio(1.33), false, null, Now)));
        await AddAsync(
            ["/media/dont-send.mkv"],
            new AspectRatioDetected(TestFileHash.For("/media/dont-send.mkv"), TestVideoFile.Detected(1.5)),
            new OverrideSaved(new Override(new AspectRatio(2.0), true, null, Now)));

        Assert.Equal(["dont-send", "override", "raw"], await ReadNamesAsync(new MediaRowView(null, null, MediaRowSort.AspectRatio, false)));
    }

    [Fact]
    public async Task A_page_reads_its_rows_and_counts_every_match()
    {
        foreach (var index in Enumerable.Range(0, 7))
        {
            await TestVideoFile.AddAsync(Store, $"/media/film-{index}.mkv", CancellationToken);
        }

        await using var session = Store.QuerySession();
        var (totalCount, rows) = await session.ReadMediaPageAsync(new MediaRowView(null, null, MediaRowSort.Path, false), 1, 3, CancellationToken);

        Assert.Equal(7, totalCount);
        Assert.Equal(["film-3", "film-4", "film-5"], rows.Select(Name));
    }

    [Fact]
    public async Task A_search_of_three_or_more_characters_matches_any_path_ignoring_case()
    {
        await AddAsync(["/media/films/Heat.mkv", "/backup/films/Heat.mkv"]);
        await AddAsync(["/media/shows/lost.mkv", "/backup/HEAT-NOTES/lost.mkv"]);
        await AddAsync(["/media/shows/other.mkv"]);

        Assert.Equal(["Heat", "lost"], await ReadNamesAsync(new MediaRowView(null, "heat", MediaRowSort.Path, false)));
        Assert.Equal(["lost"], await ReadNamesAsync(new MediaRowView(null, "  NOTES/LOST ", MediaRowSort.Path, false)));
        Assert.Equal(["Heat", "lost", "other"], await ReadNamesAsync(new MediaRowView(null, " ", MediaRowSort.Path, false)));
    }

    [Fact]
    public async Task A_shorter_search_matches_any_path_ignoring_case()
    {
        await AddAsync(["/media/x1.mkv"]);
        await AddAsync(["/media/other.mkv", "/zz/X1/other.mkv"]);
        await AddAsync(["/media/y2.mkv"]);

        Assert.Equal(["other", "x1"], await ReadNamesAsync(new MediaRowView(null, "X1", MediaRowSort.Path, false)));
        Assert.Equal(["y2"], await ReadNamesAsync(new MediaRowView(null, "2", MediaRowSort.Path, false)));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\"")]
    [InlineData("a%b")]
    [InlineData("a_b")]
    [InlineData("a\"b")]
    public async Task A_search_treats_percent_underscore_and_quotes_as_text(string term)
    {
        await AddAsync([$"/media/{term}.mkv"]);
        await AddAsync(["/media/axb.mkv"]);
        await AddAsync(["/media/plain.mkv"]);

        Assert.Equal([term], await ReadNamesAsync(new MediaRowView(null, term, MediaRowSort.Path, false)));
    }

    [Fact]
    public async Task Video_files_with_no_file_path_are_neither_listed_nor_counted()
    {
        var listed = await AddAsync(["/media/listed.mkv"]);
        await AddAsync(["/media/gone.mkv"], new FilePathRemoved(TestFileHash.For("/media/gone.mkv"), new LocalPath("/media/gone.mkv"), Now));

        await using var session = Store.QuerySession();
        var (totalCount, rows) = await session.ReadMediaPageAsync(new MediaRowView(null, null, MediaRowSort.Path, false), 0, 50, CancellationToken);

        Assert.Equal(1, totalCount);
        Assert.Equal([listed], rows.Select(row => row.FileHash));
        Assert.Equal(new Dictionary<VideoFileStatus, int> { [VideoFileStatus.Pending] = 1 }, await session.CountMediaRowsByStatusAsync(null, CancellationToken));
    }

    [Fact]
    public async Task Status_counts_count_video_files_among_the_search_matches()
    {
        await AddAsync(["/media/films/a.mkv", "/backup/films/a.mkv"], new DetectionFailed(TestFileHash.For("/media/films/a.mkv"), TestVideoFile.Failed("ffprobe failed")));
        await AddAsync(["/media/films/b.mkv", "/backup/films/b.mkv"]);
        await AddAsync(["/media/films/c.mkv"]);
        await AddAsync(["/media/shows/d.mkv"]);

        await using var session = Store.QuerySession();

        Assert.Equal(
            new Dictionary<VideoFileStatus, int> { [VideoFileStatus.Failed] = 1, [VideoFileStatus.Pending] = 2 },
            await session.CountMediaRowsByStatusAsync("films", CancellationToken));
        Assert.Equal(
            new Dictionary<VideoFileStatus, int> { [VideoFileStatus.Failed] = 1, [VideoFileStatus.Pending] = 3 },
            await session.CountMediaRowsByStatusAsync(null, CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Views))]
    public async Task Each_media_view_reads_its_page_and_its_count_through_an_index_in_order(VideoFileStatus? status, MediaRowSort sort, bool descending)
    {
        var view = new MediaRowView(status, null, sort, descending);
        await using var session = Store.QuerySession();

        var page = await session.Query<MediaRow>().MediaPage(view).Skip(50).Take(50).ExplainAsync(CancellationToken);
        var count = await session.Query<MediaRow>().Matching(view).ExplainAsync(CancellationToken);

        AssertReadsAnIndexInOrder(page);
        AssertReadsAnIndexInOrder(count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("heat")]
    [InlineData("x1")]
    public async Task Status_counts_read_an_index(string? search)
    {
        await using var session = Store.QuerySession();

        var plan = await session.Query<MediaRow>().Listed(search).GroupBy(row => row.StatusOrder).Select(group => new { group.Key, Count = group.Count() }).ExplainAsync(CancellationToken);

        Assert.True(plan.UsesIndex, plan.ToString());
    }

    [Fact]
    public async Task The_detection_queue_holds_the_pending_video_files_with_a_file_path_newest_first_from_their_most_recently_hashed_path()
    {
        var older = await AddAsync(["/media/older.mkv"], Now);
        var newer = await AddAsync(["/media/newer.mkv"], Now.AddMinutes(1));
        await AddAsync(["/media/detected.mkv"], Now.AddMinutes(2), new AspectRatioDetected(TestFileHash.For("/media/detected.mkv"), TestVideoFile.Detected(2.39)));
        await AddAsync(["/media/failed.mkv"], Now.AddMinutes(2), new DetectionFailed(TestFileHash.For("/media/failed.mkv"), TestVideoFile.Failed("ffprobe failed")));
        var manual = await AddAsync(
            ["/media/manual.mkv"],
            Now.AddSeconds(30),
            new OverrideSaved(new Override(null, true, null, Now)),
            new FilePathAdded(TestFileHash.For("/media/manual.mkv"), new LocalPath("/backup/manual.mkv"), TestVideoFile.Stat, Now.AddMinutes(3), Now.AddMinutes(3)));
        await AddAsync(["/media/no-path.mkv"], Now.AddMinutes(2), new FilePathRemoved(TestFileHash.For("/media/no-path.mkv"), new LocalPath("/media/no-path.mkv"), Now));

        await using var session = Store.QuerySession();
        var queue = await session.ReadDetectionQueueAsync(10, [], CancellationToken);

        Assert.Equal(
            [
                new DetectionRequest(newer, new LocalPath("/media/newer.mkv"), DetectionOrigin.Queue),
                new DetectionRequest(manual, new LocalPath("/backup/manual.mkv"), DetectionOrigin.Queue),
                new DetectionRequest(older, new LocalPath("/media/older.mkv"), DetectionOrigin.Queue),
            ],
            queue);
    }

    [Fact]
    public async Task The_detection_queue_leaves_out_running_video_files_and_still_fills_the_count()
    {
        var first = await AddAsync(["/media/first.mkv"], Now);
        var second = await AddAsync(["/media/second.mkv"], Now.AddMinutes(1));
        var third = await AddAsync(["/media/third.mkv"], Now.AddMinutes(2));

        await using var session = Store.QuerySession();
        var queue = await session.ReadDetectionQueueAsync(2, [third], CancellationToken);

        Assert.Equal([second, first], queue.Select(request => request.VideoFile));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_detection_queue_check_reads_its_partial_index_alone_whatever_is_running(int runningCount)
    {
        await using var session = Store.QuerySession();
        var running = Enumerable.Range(0, runningCount).Select(_ => Guid.NewGuid()).ToList();

        var plan = await session.Query<MediaRow>().DetectionQueue(running).Take(4).ExplainAsync(CancellationToken);

        var step = Assert.Single(plan.Steps);
        Assert.Contains(MediaRowProjection.DetectionQueueIndex, step.Detail);
    }

    [Fact]
    public async Task Results_from_an_older_detector_are_counted_through_their_index()
    {
        await AddAsync(["/media/older.mkv"], new AspectRatioDetected(TestFileHash.For("/media/older.mkv"), TestVideoFile.Detected(2.39, detectorVersion: 1)));
        await AddAsync(
            ["/media/redetected.mkv"],
            new AspectRatioDetected(TestFileHash.For("/media/redetected.mkv"), TestVideoFile.Detected(2.39, detectorVersion: 1)),
            new AspectRatioDetected(TestFileHash.For("/media/redetected.mkv"), TestVideoFile.Detected(2.39, detectorVersion: 2)));
        await AddAsync(["/media/pending.mkv"]);

        await using var session = Store.QuerySession();
        var plan = await session.Query<MediaRow>().ResultsBefore(2).ExplainAsync(CancellationToken);

        Assert.Equal(1, await session.CountResultsBeforeAsync(2, CancellationToken));
        Assert.Contains(plan.Steps, step => step.Detail.Contains(MediaRowProjection.DetectorVersionIndex, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Re_detect_all_reads_the_video_files_with_a_current_result_or_a_last_failure()
    {
        var detected = await AddAsync(["/media/detected.mkv"], new AspectRatioDetected(TestFileHash.For("/media/detected.mkv"), TestVideoFile.Detected(2.39)));
        var failed = await AddAsync(["/media/failed.mkv"], new DetectionFailed(TestFileHash.For("/media/failed.mkv"), TestVideoFile.Failed("ffprobe failed")));
        await AddAsync(["/media/pending.mkv"], new OverrideSaved(new Override(new AspectRatio(2.0), false, null, Now)));

        await using var session = Store.QuerySession();

        Assert.Equal(new HashSet<Guid> { detected.StreamId, failed.StreamId }, (await session.ReadVersionsWithResultOrFailureAsync(CancellationToken)).Select(videoFile => videoFile.Id).ToHashSet());
    }

    [Fact]
    public async Task A_standard_ratios_change_reads_the_current_results_with_container_metadata()
    {
        var withMetadata = await AddAsync(["/media/with.mkv"], new AspectRatioDetected(TestFileHash.For("/media/with.mkv"), TestVideoFile.Detected(2.39, containerAspectRatio: 1.78)));
        await AddAsync(["/media/without.mkv"], new AspectRatioDetected(TestFileHash.For("/media/without.mkv"), TestVideoFile.Detected(2.39)));
        await AddAsync(["/media/failed.mkv"], new DetectionFailed(TestFileHash.For("/media/failed.mkv"), TestVideoFile.Failed("ffprobe failed")));

        await using var session = Store.QuerySession();

        Assert.Equal([withMetadata], (await session.ReadWithContainerMetadataAsync(CancellationToken)).Select(row => row.FileHash));
    }

    [Fact]
    public async Task Archive_candidates_have_no_file_path_and_lost_their_last_before_the_cutoff()
    {
        await AddAsync(["/media/listed.mkv"]);
        var early = await AddAsync(["/media/early.mkv"], new FilePathRemoved(TestFileHash.For("/media/early.mkv"), new LocalPath("/media/early.mkv"), Now));
        await AddAsync(["/media/at-cutoff.mkv"], new FilePathRemoved(TestFileHash.For("/media/at-cutoff.mkv"), new LocalPath("/media/at-cutoff.mkv"), Now.AddDays(1)));

        await using var session = Store.QuerySession();

        Assert.Equal([early], await session.ReadArchiveCandidatesAsync(Now.AddDays(1), CancellationToken));
    }

    [Fact]
    public async Task Loading_media_rows_returns_one_row_for_each_hash_that_has_one()
    {
        var existing = await AddAsync(["/media/film.mkv"]);

        await using var session = Store.QuerySession();
        var missing = Enumerable.Range(0, 40_000).Select(index => TestFileHash.For($"missing-{index}"));
        var found = await session.LoadMediaRowsAsync([existing, .. missing, existing], CancellationToken);

        Assert.Equal([existing], found.Select(row => row.FileHash));
    }

    /// <summary>Asserts the plan reads an index and sorts nothing in a temporary B-tree.</summary>
    private static void AssertReadsAnIndexInOrder(QueryPlan plan)
    {
        Assert.True(plan.UsesIndex, plan.ToString());
        Assert.DoesNotContain(plan.Steps, step => step.Detail.Contains("TEMP B-TREE", StringComparison.Ordinal));
    }

    private Task<FileHash> AddAsync(IReadOnlyList<string> paths, params object[] events) => AddAsync(paths, Now, events);

    /// <summary>Discovers a video file at <paramref name="paths"/> at <paramref name="firstSeenAt"/>, with the hash <see cref="TestFileHash.For"/> gives the first, and appends <paramref name="events"/> to it.</summary>
    private async Task<FileHash> AddAsync(IReadOnlyList<string> paths, DateTimeOffset firstSeenAt, params object[] events)
    {
        var videoFile = await TestVideoFile.AddAsync(Store, paths, CancellationToken, hashedAt: firstSeenAt);
        if (events.Length > 0)
        {
            await TestVideoFile.AppendAsync(Store, videoFile, events, CancellationToken);
        }

        return videoFile;
    }

    private async Task<List<FileHash>> ReadFileHashesAsync(MediaRowView view)
    {
        await using var session = Store.QuerySession();
        return [.. (await session.ReadMediaPageAsync(view, 0, 50, CancellationToken)).Rows.Select(row => row.FileHash)];
    }

    /// <summary>The file name without its extension of each row's first path, in the view's order.</summary>
    private async Task<List<string>> ReadNamesAsync(MediaRowView view)
    {
        await using var session = Store.QuerySession();
        return [.. (await session.ReadMediaPageAsync(view, 0, 50, CancellationToken)).Rows.Select(Name)];
    }

    private static string Name(MediaRow row) => Path.GetFileNameWithoutExtension(row.FilePathsByPath.First().Path.Value);
}
