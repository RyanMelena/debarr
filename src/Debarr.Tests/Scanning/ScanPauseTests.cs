using System.Collections.Concurrent;
using System.Reactive.Linq;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Fisher;
using Fisher.Linq;
using FluentResults;
using JasperFx.Events.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Debarr.Tests.Scanning;

/// <summary>
/// Re-detect All runs inside a scan pause: it cancels the running library or folder scan, whose job runs it again once the pause ends,
/// and a scan due while it runs starts once it ends.
/// </summary>
public sealed class ScanPauseTests : IAsyncLifetime
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ScanAgain = TimeSpan.FromSeconds(10);

    private readonly FoldHook _folded = new();
    private readonly ConcurrentQueue<ActivityEvent> _scanEvents = new();
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;
    private IDisposable _subscription = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options => options.Projections.Add(_folded, ProjectionLifecycle.Inline)));
        _scanner = _host.Services.GetRequiredService<LibraryScanner>();
        _subscription = _scanner.ActivityEvents
            .Where(activityEvent => activityEvent is ScanStartedEvent or ScanStoppingEvent or ScanFinishedEvent)
            .Subscribe(_scanEvents.Enqueue);
        Directory.CreateDirectory(Root);
        await TestLibrary.AddRootFolderAsync(_host.Store, Root, CancellationToken);
        File.WriteAllText(Path.Combine(Root, "a.mkv"), "alpha");
        await _scanner.LibraryScanAsync(CancellationToken);
        Assert.True((await RecordFailedDetectionAsync()).IsSuccess);
    }

    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task Re_detect_all_sent_while_a_library_scan_runs_cancels_it_and_the_scan_runs_again_after_it()
    {
        File.WriteAllText(Path.Combine(Root, "b.mkv"), "bravo");
        var release = HoldScanAtItsFirstAddedPath();
        await _host.Scheduler.Start(CancellationToken);
        await _scanner.ScanNowAsync(CancellationToken);
        await release.Held;

        var redetect = _host.Runtime.SendCommandAsync(new RedetectAll(), CancellationToken);
        try
        {
            await Poll.UntilAsync(() => _scanner.IsStoppingScan, ScanAgain);
            Assert.False(redetect.IsCompleted, "Re-detect All should wait for the cancelled scan to end");
        }
        finally
        {
            release.Release();
        }

        Assert.True((await redetect).IsSuccess);
        Assert.Null((await LoadAlphaAsync()).LastFailure);
        await SettleUntilAsync(async () => (await ReadLibraryScanOutcomesAsync()).Count == 3);
        Assert.Equal([new LibraryScanOutcome.Finished(), new LibraryScanOutcome.Cancelled(), new LibraryScanOutcome.Finished()], await ReadLibraryScanOutcomesAsync());
        Assert.Equal(
            [new ScanStartedEvent(null), new ScanStoppingEvent(null), new ScanFinishedEvent(null, TimeSpan.Zero, null, true), new ScanStartedEvent(null), new ScanFinishedEvent(null, TimeSpan.Zero, null, false)],
            ReadScanEvents());
        Assert.Equal(2, (await TestVideoFile.ReadStoredFilePathsAsync(_host.Store, CancellationToken)).Count);
    }

    [Fact]
    public async Task Re_detect_all_sent_while_a_folder_scan_runs_cancels_it_and_the_folder_is_scanned_again_after_it()
    {
        var folder = Path.Combine(Root, "sub");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "b.mkv"), "bravo");
        var release = HoldScanAtItsFirstAddedPath();
        await _host.Scheduler.Start(CancellationToken);
        await FolderScanJob.ScheduleAsync(_host.Scheduler, folder, TimeSpan.Zero, CancellationToken);
        await release.Held;

        var redetect = _host.Runtime.SendCommandAsync(new RedetectAll(), CancellationToken);
        try
        {
            await Poll.UntilAsync(() => _scanner.IsStoppingScan, ScanAgain);
            Assert.False(redetect.IsCompleted, "Re-detect All should wait for the cancelled scan to end");
        }
        finally
        {
            release.Release();
        }

        Assert.True((await redetect).IsSuccess);
        await SettleUntilAsync(() => Task.FromResult(_scanEvents.OfType<ScanFinishedEvent>().Count() == 2));
        Assert.Equal(
            [new ScanStartedEvent(folder), new ScanStoppingEvent(folder), new ScanFinishedEvent(folder, TimeSpan.Zero, null, true), new ScanStartedEvent(folder), new ScanFinishedEvent(folder, TimeSpan.Zero, null, false)],
            ReadScanEvents());
        Assert.Equal(2, (await TestVideoFile.ReadStoredFilePathsAsync(_host.Store, CancellationToken)).Count);
    }

    [Fact]
    public async Task A_scan_due_while_re_detect_all_runs_starts_once_it_ends()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _folded.Once<DetectionResultCleared>(async () =>
        {
            held.SetResult();
            await release.Task;
            return Result.Ok();
        });
        var redetect = _host.Runtime.SendCommandAsync(new RedetectAll(), CancellationToken);
        await held.Task.WaitAsync(CancellationToken);

        var scan = _scanner.LibraryScanAsync(CancellationToken);
        try
        {
            await Task.Delay(Settle, CancellationToken);
            Assert.False(scan.IsCompleted, "the scan should wait for Re-detect All");
        }
        finally
        {
            release.SetResult();
        }

        Assert.True((await redetect).IsSuccess);
        Assert.Equal(ScanOutcome.Finished, await scan);
        Assert.Null((await LoadAlphaAsync()).LastFailure);
    }

    /// <summary>Holds the next scan inside its first path's commit, which the scan's cancellation reaches only once the commit is released.</summary>
    private ScanHold HoldScanAtItsFirstAddedPath()
    {
        var hold = new ScanHold();
        _folded.Once<FilePathAdded>(hold.HoldAsync);
        return hold;
    }

    /// <summary>Waits for the condition, and returns once it holds or <see cref="ScanAgain"/> has passed, so the assertion after it reports what happened.</summary>
    private static async Task SettleUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + ScanAgain;
        while (!await condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }
    }

    private List<ActivityEvent> ReadScanEvents() =>
        [.. _scanEvents.Select(activityEvent => activityEvent is ScanFinishedEvent finished ? finished with { Duration = TimeSpan.Zero } : activityEvent)];

    private async Task<Result> RecordFailedDetectionAsync()
    {
        var alpha = (await LoadAlphaAsync()).FileHash;
        return await _host.Runtime.SendCommandAsync(
            new RecordDetection(
                Guid.CreateVersion7(),
                alpha,
                DetectionOrigin.Queue,
                new LocalPath(Path.Combine(Root, "a.mkv")),
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                1,
                new DetectorOutcome("8.1.2", null, Result.Fail("ffprobe failed"))),
            CancellationToken);
    }

    private async Task<MediaRow> LoadAlphaAsync()
    {
        await using var session = _host.Store.QuerySession();
        var path = Path.Combine(Root, "a.mkv");
        return (await session.Query<MediaRow>().ToListAsync(CancellationToken)).Single(row => row.FilePaths.Any(filePath => filePath.Path.Value == path));
    }

    /// <summary>Every closed library scan's outcome, oldest first.</summary>
    private async Task<List<LibraryScanOutcome>> ReadLibraryScanOutcomesAsync()
    {
        await using var session = _host.Store.QuerySession();
        return [.. (await session.Query<LibraryScanSummaryRow>().ToListAsync(CancellationToken)).Where(row => row.Closed).OrderBy(row => row.StartedAt).Select(row => row.Outcome!)];
    }

    /// <summary>Holds a commit until released, and says when the commit reached the hold.</summary>
    private sealed class ScanHold
    {
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Held => _held.Task.WaitAsync(CancellationToken);

        public void Release() => _release.SetResult();

        public async Task<Result> HoldAsync()
        {
            _held.SetResult();
            await _release.Task;
            return Result.Ok();
        }
    }
}
