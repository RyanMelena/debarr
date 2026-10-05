using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Playing;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Fisher;
using FluentResults;
using JasperFx.Events.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests.EventStore;

/// <summary>
/// Each command that appends to many streams meets another commit after its inline projections folded and before its write took the lock,
/// decides again, and leaves every Media row at its stream's version with both commits in it.
/// </summary>
public sealed class VersionedAppendTests : IAsyncLifetime
{
    private const string AlphaHash = "2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192";
    private const string BravoHash = "e4e75575b66d57fcd160508154ba15328b2dd74356fb2c82d3ce75e36679fa47";

    private static readonly FileHash Alpha = new(AlphaHash);
    private static readonly FileHash Bravo = new(BravoHash);
    private static readonly LocalPath AlphaPath = new("/media/a.mkv");
    private static readonly LocalPath CopyPath = new("/media/copy of a.mkv");
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FoldHook _folded = new();
    private TestHost _host = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private IDocumentStore Store => _host.Store;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options => options.Projections.Add(_folded, ProjectionLifecycle.Inline)));
        Assert.True((await AddFilePathAsync(Alpha, AlphaPath)).IsSuccess);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Removing_file_paths_that_meets_a_detection_decides_again_and_keeps_the_detection_on_the_media_row()
    {
        _folded.Once<FilePathRemoved>(RecordFailedDetectionOfAlphaAsync);

        var removed = await SendAsync(new RemoveFilePaths([new RemoveFilePath(Alpha, AlphaPath, Start)]));

        Assert.True(removed.IsSuccess);
        Assert.Equal([nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(DetectionFailed), nameof(FilePathRemoved)], await ReadEventTypeNamesAsync(Alpha));
        var row = await LoadRowAsync(Alpha);
        Assert.NotNull(row.LastFailure);
        Assert.Empty(row.FilePaths);
        Assert.Equal(await ReadStreamVersionAsync(Alpha), row.Version);
    }

    [Fact]
    public async Task Removing_a_file_path_that_moved_to_another_video_file_leaves_both_alone()
    {
        Assert.True((await SendAsync(new RemoveFilePath(Alpha, AlphaPath, Start))).IsSuccess);
        Assert.True((await AddFilePathAsync(Bravo, AlphaPath)).IsSuccess);

        var removed = await SendAsync(new RemoveFilePaths([new RemoveFilePath(Alpha, AlphaPath, Start)]));

        Assert.True(removed.IsSuccess);
        Assert.Equal([nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(FilePathRemoved)], await ReadEventTypeNamesAsync(Alpha));
        await using var session = Store.QuerySession();
        Assert.Equal(Bravo, (await session.LoadAsync<StoredFilePath>(AlphaPath.Value, CancellationToken))?.VideoFile);
    }

    [Fact]
    public async Task Re_detect_all_that_meets_a_scan_decides_again_and_keeps_the_scans_path_on_the_media_row()
    {
        Assert.True((await RecordFailedDetectionOfAlphaAsync()).IsSuccess);
        _folded.Once<DetectionResultCleared>(() => AddFilePathAsync(Alpha, CopyPath));

        var redetected = await SendAsync(new RedetectAll());

        Assert.True(redetected.IsSuccess);
        Assert.Equal(
            [nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(DetectionFailed), nameof(FilePathAdded), nameof(DetectionResultCleared)],
            await ReadEventTypeNamesAsync(Alpha));
        var row = await LoadRowAsync(Alpha);
        Assert.Null(row.LastFailure);
        Assert.Equal(2, row.FilePaths.Count);
        Assert.Equal(await ReadStreamVersionAsync(Alpha), row.Version);
    }

    [Fact]
    public async Task A_standard_ratios_change_that_meets_a_scan_decides_again_and_keeps_the_scans_path_on_the_media_row()
    {
        await TestVideoFile.AppendAsync(Store, Alpha, [new AspectRatioDetected(Alpha, FromFileDetection(1.85))], CancellationToken);
        _folded.Once<DetectionResultCleared>(() => AddFilePathAsync(Alpha, CopyPath));

        var saved = await SendAsync(await MarkCheckPictureAsync(1.85));

        Assert.True(saved.IsSuccess);
        Assert.Equal(
            [nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(AspectRatioDetected), nameof(FilePathAdded), nameof(DetectionResultCleared)],
            await ReadEventTypeNamesAsync(Alpha));
        var row = await LoadRowAsync(Alpha);
        Assert.Null(row.CurrentResult);
        Assert.Equal(2, row.FilePaths.Count);
        Assert.Equal(await ReadStreamVersionAsync(Alpha), row.Version);
    }

    [Fact]
    public async Task Two_clears_of_the_history_at_once_both_commit()
    {
        _folded.Once<HistoryCleared>(() => SendAsync(new ClearHistory()));

        var cleared = await SendAsync(new ClearHistory());

        Assert.True(cleared.IsSuccess);
        await using var session = Store.QuerySession();
        var clears = await session.Events.FetchStreamAsync(ClearHistoryHandler.HistoryStreamId, token: CancellationToken);
        Assert.Equal(2, clears.Count);
        Assert.Equal(((HistoryCleared)clears[^1].Data).ClearedAt, (await session.LoadAsync<HistoryClear>(ClearHistoryHandler.HistoryStreamId, CancellationToken))!.ClearedAt);
    }

    private Task<Result> SendAsync(object command) => _host.Runtime.SendCommandAsync(command, CancellationToken);

    private Task<Result> AddFilePathAsync(FileHash videoFile, LocalPath path) => SendAsync(new AddFilePath(videoFile, path, new FileStat(1, Start), Start));

    private Task<Result> RecordFailedDetectionOfAlphaAsync() => SendAsync(new RecordDetection(
        Guid.CreateVersion7(),
        Alpha,
        DetectionOrigin.Queue,
        AlphaPath,
        Start,
        TimeSpan.FromSeconds(1),
        1,
        new DetectorOutcome("8.1.2", null, Result.Fail("ffprobe failed"))));

    private static Detection FromFileDetection(double containerAspectRatio) => new(
        Guid.CreateVersion7(),
        DetectionOrigin.Queue,
        AlphaPath,
        Start,
        TimeSpan.FromSeconds(1),
        1,
        "8.1.2",
        new ContainerMetadata(new AspectRatio(containerAspectRatio), 1920, 1080, "hevc", "bt709"),
        new DetectionOutcome.Succeeded(DetectionResult.FromFile(new AspectRatio(containerAspectRatio))));

    /// <summary>The stored detection settings as the page sends them, with one standard ratio marked Check Picture.</summary>
    private async Task<ChangeDetectionSettings> MarkCheckPictureAsync(double aspectRatio)
    {
        await using var session = Store.QuerySession();
        var settings = await DetectionSettings.ReadAsync(session, CancellationToken);
        return new ChangeDetectionSettings(
            settings.SimultaneousDetections,
            settings.PictureMeasurement.SampleCount,
            settings.PictureMeasurement.SkipStartAndEndPercent,
            settings.TimeoutSeconds,
            settings.PictureMeasurement.BlackLevelSdr,
            settings.PictureMeasurement.BlackLevelHdr,
            [.. settings.StandardRatios.Ratios.Select(standardRatio => standardRatio.AspectRatio == aspectRatio ? standardRatio with { ChecksPicture = true } : standardRatio)],
            settings.StandardRatios.MatchTolerance);
    }

    private async Task<MediaRow> LoadRowAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return (await session.LoadAsync<MediaRow>(videoFile.StreamId, CancellationToken))!;
    }

    private async Task<long> ReadStreamVersionAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return (await session.Events.FetchStreamStateAsync(videoFile.StreamId, CancellationToken))!.Version;
    }

    private async Task<List<string>> ReadEventTypeNamesAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Events.FetchStreamAsync(videoFile.StreamId, token: CancellationToken)).Select(stored => stored.Data.GetType().Name)];
    }
}
