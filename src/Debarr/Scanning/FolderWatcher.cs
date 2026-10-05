using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using Debarr.EventStore;
using Debarr.Extensions;
using Fisher;

namespace Debarr.Scanning;

/// <summary>
/// Schedules a folder scan of each folder where files change, under every enabled root folder, from startup to shutdown.
/// It watches only while the watch setting is on, and reads the root folders again after each commit that changes the library.
/// </summary>
public sealed partial class FolderWatcher : IHostedService, IDisposable
{
    private readonly IDocumentStore _store;

    private readonly Quartz.ISchedulerFactory _schedulerFactory;

    private readonly ILogger<FolderWatcher> _logger;

    private readonly IObservable<IReadOnlyList<string>> _watchedRootFolderPaths;

    private readonly CompositeDisposable _disposables = [];

    public FolderWatcher(
        IDocumentStore store,
        Quartz.ISchedulerFactory schedulerFactory,
        ReadModelChangeListener readModelChangeListener,
        ILogger<FolderWatcher> logger)
    {
        _store = store;
        _schedulerFactory = schedulerFactory;
        _logger = logger;

        _watchedRootFolderPaths = readModelChangeListener.ChangesTo(nameof(Library))
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => Observable.FromAsync(GetWatchedRootFolderPathsAsync))
            .Switch()
            .OfType<IReadOnlyList<string>>()
            // The key joins the paths with a character no path holds.
            .DistinctUntilChanged(rootPaths => string.Join('\0', rootPaths))
            .Select(Watch)
            .Switch();
    }

    /// <summary>How long a folder goes without a change before its folder scan runs.</summary>
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Starts watching the enabled root folders, before the host accepts requests, and logs each list of root folders once its watchers run.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watchedRootFolderPaths
            .CompleteOnError(LogWatchStopped)
            .Subscribe(LogWatching)
            .DisposeWith(_disposables);
        return Task.CompletedTask;
    }

    /// <summary>Closes every watcher.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _disposables.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _disposables.Dispose();

    /// <summary>The paths of the enabled root folders that exist, none while the watch is off, or null when the read fails.</summary>
    private async Task<IReadOnlyList<string>?> GetWatchedRootFolderPathsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = _store.QuerySession();
            var library = await Library.ReadAsync(session, cancellationToken);
            if (!library.Settings.WatchFolders)
            {
                return [];
            }

            var rootPaths = library.RootFolders.Where(root => root.Enabled).Select(root => root.Path.Value).ToList();
            foreach (var missing in rootPaths.Where(rootPath => !Directory.Exists(rootPath)))
            {
                LogRootFolderMissing(missing);
            }

            return [.. rootPaths.Where(Directory.Exists)];
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRootFoldersUnread(exception);
            return null;
        }
    }

    /// <summary>Watches each root while subscribed, and emits the root paths once every watcher runs.</summary>
    private IObservable<IReadOnlyList<string>> Watch(IReadOnlyList<string> rootPaths) =>
        Observable
            .Merge(rootPaths.Select(WatchRootFolder))
            // Holds the watchers open, and passes on none of their elements.
            .IgnoreElements()
            .Select(_ => rootPaths)
            // Merge subscribes in order, so the root paths follow once every watcher runs.
            .Merge(Observable.Return(rootPaths));

    /// <summary>Watches the root while subscribed, and schedules a folder scan for each change and a library scan after each watcher error.</summary>
    private IObservable<Unit> WatchRootFolder(string rootPath) =>
        Observable
            .Using(
                () => new FileSystemWatcher(rootPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                },
                watcher =>
                {
                    // Scheduler.Immediate adds and removes the handlers on the calling thread, which can be a Blazor circuit's.
                    var created = FromFileSystemEvent(handler => watcher.Created += handler, handler => watcher.Created -= handler);
                    var deleted = FromFileSystemEvent(handler => watcher.Deleted += handler, handler => watcher.Deleted -= handler);

                    // A folder's write time changes with the files in it, so a changed folder would schedule a scan of its parent.
                    var changed = FromFileSystemEvent(handler => watcher.Changed += handler, handler => watcher.Changed -= handler)
                        .Where(change => !Directory.Exists(change.FullPath));

                    var renamed = Observable
                        .FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
                            handler => watcher.Renamed += handler,
                            handler => watcher.Renamed -= handler,
                            Scheduler.Immediate)
                        .SelectMany(eventPattern => new[] { eventPattern.EventArgs.OldFullPath, eventPattern.EventArgs.FullPath });

                    var errors = Observable
                        .FromEventPattern<ErrorEventHandler, ErrorEventArgs>(
                            handler => watcher.Error += handler,
                            handler => watcher.Error -= handler,
                            Scheduler.Immediate)
                        .Select(eventPattern => Observable.FromAsync(cancellationToken => ScanAfterErrorAsync(rootPath, eventPattern.EventArgs.GetException(), cancellationToken)));

                    var folderScans = Observable.Merge(created, deleted, changed)
                        .Select(change => change.FullPath)
                        .Merge(renamed)
                        .Select(path => Path.GetDirectoryName(path))
                        .OfType<string>()
                        .Select(folder => Observable.FromAsync(cancellationToken => ScheduleScanFolderAsync(folder, cancellationToken)));

                    return folderScans.Merge(errors).Concat();
                })
            .Catch((Exception exception) =>
            {
                LogWatchFailed(exception, rootPath);
                return Observable.Empty<Unit>();
            });

    private static IObservable<FileSystemEventArgs> FromFileSystemEvent(
        Action<FileSystemEventHandler> addHandler,
        Action<FileSystemEventHandler> removeHandler) =>
        Observable
            .FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(addHandler, removeHandler, Scheduler.Immediate)
            .Select(eventPattern => eventPattern.EventArgs);

    private async Task<Unit> ScheduleScanFolderAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            await FolderScanJob.ScheduleAsync(scheduler, folder, QuietPeriod, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFolderScanUnscheduled(exception, folder);
        }

        return Unit.Default;
    }

    private async Task<Unit> ScanAfterErrorAsync(string rootPath, Exception watchError, CancellationToken cancellationToken)
    {
        LogWatchMissedChanges(watchError, rootPath);
        try
        {
            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            await LibraryScanJob.TriggerNowAsync(scheduler, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogLibraryScanUnstarted(exception, rootPath);
        }

        return Unit.Default;
    }

    private void LogWatching(IReadOnlyList<string> rootPaths)
    {
        if (rootPaths.Count == 0)
        {
            LogWatchingNone();
        }
        else
        {
            LogWatching(string.Join(", ", rootPaths));
        }
    }

    [LoggerMessage(LogLevel.Information, "Watching {RootFolders}.")]
    private partial void LogWatching(string rootFolders);

    [LoggerMessage(LogLevel.Information, "Watching no root folder.")]
    private partial void LogWatchingNone();

    [LoggerMessage(LogLevel.Error, "The watch of the root folders stopped.")]
    private partial void LogWatchStopped(Exception exception);

    [LoggerMessage(LogLevel.Warning, "The root folder {RootFolder} is missing, so the watch skips it.")]
    private partial void LogRootFolderMissing(string rootFolder);

    [LoggerMessage(LogLevel.Error, "Could not read the root folders to watch, so the watch stays as it was.")]
    private partial void LogRootFoldersUnread(Exception exception);

    [LoggerMessage(LogLevel.Error, "Could not watch the root folder {RootFolder}.")]
    private partial void LogWatchFailed(Exception exception, string rootFolder);

    [LoggerMessage(LogLevel.Error, "Could not schedule a folder scan of {Folder}.")]
    private partial void LogFolderScanUnscheduled(Exception exception, string folder);

    [LoggerMessage(LogLevel.Warning, "The watch of {RootFolder} missed changes, so a library scan runs.")]
    private partial void LogWatchMissedChanges(Exception exception, string rootFolder);

    [LoggerMessage(LogLevel.Error, "Could not start a library scan after the watch of {RootFolder} failed.")]
    private partial void LogLibraryScanUnstarted(Exception exception, string rootFolder);
}
