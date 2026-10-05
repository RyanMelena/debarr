using Debarr.Detecting;
using Debarr.Scanning;
using Debarr.Tests.Detecting;
using Debarr.Tests.EventStore;
using Fisher;
using Fisher.Linq;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using IDocumentSessionOperations = JasperFx.Events.Documents.IDocumentSessionOperations;

namespace Debarr.Tests.Scanning;

/// <summary>
/// A root folder removal runs inside a scan pause and a detection pause: it cancels the running library scan, whose job runs it again once the removal ends,
/// and a library scan due while it runs starts once it ends.
/// A file scan runs during it, and its write can meet the removal's archive.
/// </summary>
public sealed class RootFolderRemovalRaceTests : IAsyncLifetime
{
    private const string AlphaHash = "2ed1fd7bf3f00721f696494246138cbdcbd917f1ea85924be1ec0e86096f9192";

    private static readonly DateTime WrittenAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);
    private static readonly FileHash Alpha = new(AlphaHash);

    private readonly Interleaver _interleaver = new();
    private TestHost _host = null!;
    private LibraryScanner _scanner = null!;
    private RootFolderRemover _remover = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_host.DataDirectory, "root");

    private string Other => Path.Combine(_host.DataDirectory, "other");

    private IDocumentStore Store => _host.Store;

    public async ValueTask InitializeAsync()
    {
        _host = await TestHost.StartAsync(services => services.ConfigureFisher(options => options.Listeners.Add(_interleaver)));
        _scanner = _host.Services.GetRequiredService<LibraryScanner>();
        _remover = _host.Services.GetRequiredService<RootFolderRemover>();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Other);
        await TestLibrary.AddRootFolderAsync(Store, Root, CancellationToken);
        await TestLibrary.AddRootFolderAsync(Store, Other, CancellationToken);

        // The first scan stores a file under each root folder, and each test removes the first root folder.
        Write(Root, "a.mkv", "alpha");
        Write(Other, "b.mkv", "bravo");
        await _scanner.LibraryScanAsync(CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task A_root_folder_removal_cancels_the_running_library_scan_which_runs_again_after_it()
    {
        Write(Other, "c.mkv", "charlie");
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _interleaver.Before(session => AddsPath(session, "c.mkv"), async () =>
        {
            held.SetResult();
            await release.Task;
            return Result.Ok();
        });
        await _host.Scheduler.Start(CancellationToken);
        await _scanner.ScanNowAsync(CancellationToken);
        await held.Task.WaitAsync(CancellationToken);

        await TestLibrary.AppendAsync(Store, [new RootFolderRemoved(new LocalPath(Root))], CancellationToken);
        var removal = Task.Run(() => _remover.RemoveAsync(new DirectoryInfo(Root), CancellationToken), CancellationToken);
        try
        {
            await Poll.UntilAsync(() => _scanner.IsStoppingScan, TimeSpan.FromSeconds(10));
            Assert.False(removal.IsCompleted, "the removal should wait for the cancelled scan to end");
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(1, await removal);
        await Poll.UntilAsync(async () => (await ReadLibraryScanOutcomesAsync()).Count == 3, TimeSpan.FromSeconds(10));
        Assert.Equal([new LibraryScanOutcome.Finished(), new LibraryScanOutcome.Cancelled(), new LibraryScanOutcome.Finished()], await ReadLibraryScanOutcomesAsync());
        Assert.True(await IsArchivedAsync(Alpha), "alpha should stay archived after the scan runs again");
        Assert.Equal(
            [Path.Combine(Other, "b.mkv"), Path.Combine(Other, "c.mkv")],
            (await TestVideoFile.ReadStoredFilePathsAsync(Store, CancellationToken)).Select(filePath => filePath.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_library_scan_due_during_a_root_folder_removal_starts_once_it_ends()
    {
        await TestLibrary.AppendAsync(Store, [new RootFolderRemoved(new LocalPath(Root))], CancellationToken);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _interleaver.Before<VideoFileArchived>(async () =>
        {
            held.SetResult();
            await release.Task;
            return Result.Ok();
        });
        var removal = Task.Run(() => _remover.RemoveAsync(new DirectoryInfo(Root), CancellationToken), CancellationToken);
        await held.Task.WaitAsync(CancellationToken);

        var scan = _scanner.LibraryScanAsync(CancellationToken);
        try
        {
            await Task.Delay(Settle, CancellationToken);
            Assert.False(scan.IsCompleted, "the scan should wait for the removal");
            Assert.Single(await ReadLibraryScanOutcomesAsync());
        }
        finally
        {
            release.SetResult();
        }

        Assert.Equal(1, await removal);
        Assert.Equal(ScanOutcome.Finished, await scan);
        Assert.Equal([new LibraryScanOutcome.Finished(), new LibraryScanOutcome.Finished()], await ReadLibraryScanOutcomesAsync());
        Assert.True(await IsArchivedAsync(Alpha));
    }

    /// <summary>
    /// A file scan read a.mkv as stored and missing, then the whole removal runs, removing the path and archiving alpha,
    /// before the file scan's RemoveFilePath commits.
    /// </summary>
    [Fact]
    public async Task A_file_scan_whose_video_file_is_archived_before_it_removes_the_missing_path_returns_no_video_file()
    {
        File.Delete(Path.Combine(Root, "a.mkv"));
        var raced = false;
        _interleaver.Before(session => RemovesPath(session, "a.mkv"), async () =>
        {
            raced = true;
            await TestLibrary.AppendAsync(Store, [new RootFolderDisabled(new LocalPath(Root))], CancellationToken);
            await _remover.RemoveAsync(new DirectoryInfo(Root), CancellationToken);
            return Result.Ok();
        });

        var videoFile = await _scanner.ScanFileAsync(new LocalPath(Path.Combine(Root, "a.mkv")), CancellationToken);

        Assert.Null(videoFile);
        Assert.True(raced, "the file scan should have sent its RemoveFilePath");
        Assert.Equal(
            [nameof(VideoFileDiscovered), nameof(FilePathAdded), nameof(FilePathRemoved), nameof(VideoFileArchived)],
            await ReadEventTypeNamesAsync(Alpha));
        Assert.Equal([Path.Combine(Other, "b.mkv")], (await TestVideoFile.ReadStoredFilePathsAsync(Store, CancellationToken)).Select(filePath => filePath.Id));
    }

    private static bool AddsPath(IDocumentSessionOperations session, string fileName) =>
        session.PendingStreams.SelectMany(stream => stream.Events)
            .Any(pending => pending.Data is FilePathAdded added && added.Path.Value.EndsWith(fileName, StringComparison.Ordinal));

    private static bool RemovesPath(IDocumentSessionOperations session, string fileName) =>
        session.PendingStreams.SelectMany(stream => stream.Events)
            .Any(pending => pending.Data is FilePathRemoved removed && removed.Path.Value.EndsWith(fileName, StringComparison.Ordinal));

    private static void Write(string folder, string fileName, string content)
    {
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, WrittenAt);
    }

    private async Task<List<string>> ReadEventTypeNamesAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Events.FetchStreamAsync(videoFile.StreamId, token: CancellationToken)).Select(stored => stored.Data.GetType().Name)];
    }

    /// <summary>Every closed library scan's outcome, oldest first.</summary>
    private async Task<List<LibraryScanOutcome>> ReadLibraryScanOutcomesAsync()
    {
        await using var session = Store.QuerySession();
        return [.. (await session.Query<LibraryScanSummaryRow>().ToListAsync(CancellationToken)).Where(row => row.Closed).OrderBy(row => row.StartedAt).Select(row => row.Outcome!)];
    }

    private async Task<bool> IsArchivedAsync(FileHash videoFile)
    {
        await using var session = Store.QuerySession();
        return (await session.Events.FetchStreamStateAsync(videoFile.StreamId, CancellationToken))!.IsArchived;
    }
}
