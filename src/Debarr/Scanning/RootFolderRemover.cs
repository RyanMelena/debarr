using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;
using Debarr.Activity;
using Debarr.Detecting;
using Fisher;
using Quartz;

namespace Debarr.Scanning;

/// <summary>
/// Runs the removal of each root folder that was removed or disabled, one at a time, in the background:
/// it removes the root folder's file paths and archives the video files they leave with none.
/// Stopping the host cancels the running removal, and a later library scan removes the file paths it left.
/// </summary>
public sealed partial class RootFolderRemover(
    IDocumentStore store,
    LibraryScanner libraryScanner,
    DetectionOrchestrator detectionOrchestrator,
    ISchedulerFactory schedulerFactory,
    TimeProvider timeProvider,
    ILogger<RootFolderRemover> logger) : BackgroundService, IActivitySource
{
    private readonly Channel<LocalPath> _queue = Channel.CreateUnbounded<LocalPath>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ISubject<ActivityEvent> _subject = Subject.Synchronize(new Subject<ActivityEvent>());

    private RootFolderRemovalStartedEvent? _runningRootFolderRemoval;

    // The root folders whose removal is queued or running, by full path.
    private readonly ConcurrentDictionary<string, byte> _rootFoldersBeingRemoved = new(StringComparer.Ordinal);

    /// <summary>A <see cref="RootFolderRemovalStartedEvent"/> and a <see cref="RootFolderRemovalFinishedEvent"/> for each root folder's removal.</summary>
    public IObservable<ActivityEvent> ActivityEvents => _subject.AsObservable();

    /// <summary>
    /// The root folder whose removal is running, and when it started; null when none is running.
    /// A removal queued while none runs shows as running from when it was queued.
    /// </summary>
    public RootFolderRemovalStartedEvent? RunningRootFolderRemoval => Volatile.Read(ref _runningRootFolderRemoval);

    public static bool ChangesRunningRootFolderRemoval(ActivityEvent activityEvent) =>
        activityEvent is RootFolderRemovalStartedEvent or RootFolderRemovalFinishedEvent;

    /// <summary>Whether the removal of the root folder is queued or running.</summary>
    public bool IsRemoving(LocalPath rootFolder) =>
        _rootFoldersBeingRemoved.ContainsKey(new DirectoryInfo(rootFolder.Value).FullName)
        || RunningRootFolderRemoval?.RootFolder == new DirectoryInfo(rootFolder.Value).FullName;

    /// <summary>Unschedules the folder scans under the root folder, then queues the removal of its file paths, which runs after the removals queued before it.</summary>
    public async Task EnqueueAsync(LocalPath rootFolder, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(rootFolder.Value);
        await FolderScanJob.UnscheduleUnderAsync(await schedulerFactory.GetScheduler(cancellationToken), directory, cancellationToken);
        Interlocked.CompareExchange(ref _runningRootFolderRemoval, new RootFolderRemovalStartedEvent(directory.FullName, timeProvider.GetUtcNow()), null);
        _rootFoldersBeingRemoved.TryAdd(directory.FullName, 0);
        _queue.Writer.TryWrite(rootFolder);
    }

    /// <summary>
    /// Removes every file path under the root folder, then archives the video files those paths belonged to
    /// that are left with no file path, and returns how many it archived.
    /// Holds a library write pause until it finishes or fails.
    /// </summary>
    public async Task<int> RemoveAsync(DirectoryInfo rootFolder, CancellationToken cancellationToken)
    {
        var started = new RootFolderRemovalStartedEvent(rootFolder.FullName, timeProvider.GetUtcNow());
        Volatile.Write(ref _runningRootFolderRemoval, started);
        _subject.OnNext(started);
        string? error = null;
        try
        {
            using var pause = await LibraryWritePause.StartAsync(libraryScanner, detectionOrchestrator, cancellationToken);
            IReadOnlyList<StoredFilePath> stored;
            await using (var session = store.QuerySession())
            {
                stored = await session.ReadStoredUnderAsync(rootFolder.FullName, cancellationToken);
            }

            await libraryScanner.RemoveFilePathsAsync(stored, null, cancellationToken);
            return await libraryScanner.ArchiveAsync([.. stored.Select(filePath => filePath.VideoFile).Distinct()], cancellationToken);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            throw;
        }
        finally
        {
            _rootFoldersBeingRemoved.TryRemove(rootFolder.FullName, out _);
            Volatile.Write(ref _runningRootFolderRemoval, null);
            _subject.OnNext(new RootFolderRemovalFinishedEvent(rootFolder.FullName, error));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var rootFolder in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RemoveAsync(new DirectoryInfo(rootFolder.Value), stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRemovalFailed(exception, rootFolder.Value);
            }
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not remove the file paths under {RootFolder}.")]
    private partial void LogRemovalFailed(Exception exception, string rootFolder);
}
