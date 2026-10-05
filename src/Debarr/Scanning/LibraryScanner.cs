using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Debarr.Activity;
using Debarr.Detecting;
using Debarr.EventStore;
using Debarr.Extensions;
using Fisher;
using FluentResults;
using JasperFx.Events;
using Quartz;
using Wolverine.Runtime;

namespace Debarr.Scanning;

/// <summary>
/// Finds the video files under the enabled root folders and records the file paths of each one.
/// It runs library, folder and file scans, hashes new and changed files, restores archived video files a scan finds again,
/// and archives the video files left with no file path.
/// </summary>
public sealed partial class LibraryScanner(
    IDocumentStore store,
    IWolverineRuntime runtime,
    TimeProvider timeProvider,
    ISchedulerFactory schedulerFactory,
    ILogger<LibraryScanner> logger) : IActivitySource
{
    /// <summary>The job group of both scan jobs and the execution group of every scan trigger, limited to one at a time.</summary>
    public const string ExecutionGroup = "scan";

    /// <summary>How many file paths or video files one command removes or archives.</summary>
    private const int BatchSize = 500;

    /// <summary>How many files one scan hashes at a time, leaving a network share room to stream playback.</summary>
    private const int HashParallelism = 4;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,

        // Hidden and system files with a video extension are video files too.
        AttributesToSkip = 0,
    };

    // Scans run concurrently, so the subject serializes their events.
    private readonly ISubject<ActivityEvent> _subject = Subject.Synchronize(new Subject<ActivityEvent>());

    // When a file scan last found each path, so a library or folder scan running at the time keeps it.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _fileScanFinds = new();

    // The scans of one hash store its file paths one at a time, so each knows whether it added the path or the video file.
    private readonly VideoFileLocks _videoFileLocks = new();

    // Guards the running scans and the paused flag, so a start never races a pause.
    private readonly Lock _lock = new();

    // The library and folder scans running now, which Quartz runs one at a time.
    private readonly List<RunningScan> _runningScans = [];

    // One scan pause at a time.
    private readonly SemaphoreSlim _pauseLock = new(1, 1);

    private bool _paused;

    // Completes when the scan pause ends, which starts the scans it held.
    private TaskCompletionSource _pauseEnded = new();

    /// <summary>
    /// A <see cref="ScanStartedEvent"/> and a <see cref="ScanFinishedEvent"/> for each scan job,
    /// a <see cref="ScanStoppingEvent"/> for each scan a scan pause stops,
    /// and a <see cref="ScheduleChanged"/> for each change to the library scan's schedule.
    /// </summary>
    public IObservable<ActivityEvent> ActivityEvents => field ??= _subject
        .AsObservable()
        .Merge(CreateSchedulerEvents(schedulerFactory).Publish().RefCount());

    /// <summary>Runs a library scan now, or once the running one ends, unless one is already waiting to run.</summary>
    public async Task ScanNowAsync(CancellationToken cancellationToken) =>
        await LibraryScanJob.TriggerNowAsync(await schedulerFactory.GetScheduler(cancellationToken), cancellationToken);

    /// <summary>When the scheduled library scan runs next; null while the schedule is off.</summary>
    public async Task<DateTimeOffset?> ReadNextScheduledScanAsync(CancellationToken cancellationToken) =>
        await LibraryScanJob.ReadNextScheduledRunAsync(await schedulerFactory.GetScheduler(cancellationToken), cancellationToken);

    public static bool ChangesNextScheduledScan(ActivityEvent activityEvent) =>
        activityEvent is ScanStartedEvent { Folder: null } or ScanFinishedEvent { Folder: null } or ScheduleChanged;

    /// <summary>Whether a scan pause is stopping the running library or folder scan.</summary>
    public bool IsStoppingScan
    {
        get
        {
            lock (_lock)
            {
                return _paused && _runningScans.Count > 0;
            }
        }
    }

    public static bool StopsLibraryScan(ActivityEvent activityEvent) => activityEvent is ScanStoppingEvent { Folder: null };

    /// <summary>
    /// Starts a scan pause: cancels the running library or folder scan, waits for it to end, and starts none until the pause is disposed.
    /// The cancelled scan ends cancelled and its job runs it again once the pause ends, and a scan due during the pause starts then.
    /// </summary>
    public async Task<ScanPause> PauseScansAsync(CancellationToken cancellationToken)
    {
        await _pauseLock.WaitAsync(cancellationToken);
        try
        {
            List<RunningScan> running;
            lock (_lock)
            {
                _paused = true;
                _pauseEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                running = [.. _runningScans];
            }

            foreach (var scan in running)
            {
                scan.Stop();
                _subject.OnNext(new ScanStoppingEvent(scan.Folder));
            }

            await Task.WhenAll(running.Select(scan => scan.Ended)).WaitAsync(cancellationToken);
        }
        catch
        {
            ResumeScans();
            throw;
        }

        return new ScanPause(ResumeScans);
    }

    /// <summary>
    /// Starts a library scan on a stream of its own, scans every enabled root folder, removes the file paths under no enabled root folder,
    /// archives the video files those paths leave with none and those whose last file path left before the scan started,
    /// and ends the scan with its counts, finished, failed or cancelled.
    /// A scan pause cancels the scan, which returns cancelled; the caller's token cancels it with an exception.
    /// </summary>
    public async Task<ScanOutcome> LibraryScanAsync(CancellationToken cancellationToken)
    {
        using var running = await StartScanAsync(null, cancellationToken);
        var libraryScanId = Guid.CreateVersion7();
        var startedAt = timeProvider.GetUtcNow();
        ForgetFileScansBefore(startedAt);
        var tally = new ScanTally();
        var videoFilesArchived = 0;
        LibraryScanOutcome outcome = new LibraryScanOutcome.Finished();

        try
        {
            await RecordAsync(new StartLibraryScan(libraryScanId, startedAt));
            var scope = await ReadScanScopeAsync(running.Token);
            foreach (var root in scope.RootFolders)
            {
                await ScanRootFolderAsync(scope, libraryScanId, root, startedAt, tally, running.Token);
            }

            var outside = await ReadStoredOutsideAsync(await ReadScanScopeAsync(running.Token), running.Token);
            tally.AddDeleted(await RemoveFilePathsAsync(outside, null, running.Token));

            IReadOnlyList<FileHash> leftBeforeScan;
            await using (var session = store.QuerySession())
            {
                leftBeforeScan = await session.ReadArchiveCandidatesAsync(startedAt, running.Token);
            }

            videoFilesArchived = await ArchiveAsync([.. outside.Select(filePath => filePath.VideoFile).Concat(leftBeforeScan).Distinct()], running.Token);
        }
        catch (OperationCanceledException) when (running.Token.IsCancellationRequested)
        {
            outcome = new LibraryScanOutcome.Cancelled();
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return ScanOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            outcome = new LibraryScanOutcome.Failed(exception.Message);
            throw;
        }
        finally
        {
            // A cancelled scan ends with its counts too.
            await RecordAsync(new EndLibraryScan(
                libraryScanId,
                timeProvider.GetUtcNow(),
                new LibraryScanCounts(tally.FilePathsFound, tally.FilesHashed, tally.VideoFilesAdded, tally.FilePathsDeleted, videoFilesArchived),
                outcome));
        }

        return ScanOutcome.Finished;
    }

    /// <summary>
    /// Scans one folder under an enabled root folder, which can be the whole root folder.
    /// A scan pause cancels the scan, which returns cancelled; the caller's token cancels it with an exception.
    /// </summary>
    public async Task<ScanOutcome> ScanFolderAsync(DirectoryInfo folder, CancellationToken cancellationToken)
    {
        using var running = await StartScanAsync(folder.FullName, cancellationToken);
        try
        {
            var scanStartedAt = timeProvider.GetUtcNow();
            ForgetFileScansBefore(scanStartedAt);
            var scope = await ReadScanScopeAsync(running.Token);

            if (scope.RootFolders.FirstOrDefault(folder.IsSameOrUnder) is { } root && IsMounted(root))
            {
                await ScanFilesAsync(scope, folder, scanStartedAt, new ScanTally(), running.Token);
            }

            return ScanOutcome.Finished;
        }
        catch (OperationCanceledException) when (running.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ScanOutcome.Cancelled;
        }
    }

    /// <summary>
    /// Scans one file and returns its video file.
    /// Null when the path is outside every enabled root folder, has an extension outside the video extensions,
    /// or has no readable file, which removes its file path.
    /// </summary>
    public async Task<FileHash?> ScanFileAsync(LocalPath path, CancellationToken cancellationToken)
    {
        var foundAt = timeProvider.GetUtcNow();
        var scope = await ReadScanScopeAsync(cancellationToken);

        var file = new FileInfo(path.Value);
        if (!scope.RootFolders.Any(file.IsSameOrUnder) || !scope.VideoExtensions.Includes(file))
        {
            return null;
        }

        StoredFilePath? stored;
        await using (var session = store.QuerySession())
        {
            stored = await session.LoadAsync<StoredFilePath>(file.FullName, cancellationToken);
        }

        if (file.Exists)
        {
            // A library or folder scan running now keeps the path, whatever it found.
            _fileScanFinds[file.FullName] = foundAt;
            if (stored is not null && stored.Stat.Matches(file))
            {
                return stored.VideoFile;
            }

            if (await HashAsync(file, cancellationToken) is { } hashed)
            {
                return hashed.VideoFile;
            }
        }

        if (stored is not null)
        {
            await SendAsync(new RemoveFilePath(stored.VideoFile, new LocalPath(file.FullName), timeProvider.GetUtcNow()), cancellationToken);
        }

        return null;
    }

    /// <summary>Scans a whole root folder for the library scan, and records the root folder's scan as it finishes.</summary>
    private async Task ScanRootFolderAsync(
        ScanScope scope,
        Guid libraryScanId,
        DirectoryInfo root,
        DateTimeOffset scanStartedAt,
        ScanTally tally,
        CancellationToken cancellationToken)
    {
        var path = new LocalPath(root.FullName);
        if (!IsMounted(root))
        {
            await RecordAsync(new RecordRootFolderScan(libraryScanId, path, "The folder is missing."));
            return;
        }

        try
        {
            await ScanFilesAsync(scope, root, scanStartedAt, tally, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordAsync(new RecordRootFolderScan(libraryScanId, path, exception.Message));
            throw;
        }

        await RecordAsync(new RecordRootFolderScan(libraryScanId, path, null));
    }

    /// <summary>Whether the root folder exists, logging a warning when it is missing.</summary>
    /// <remarks>An unmounted share looks like an empty root folder, so a scan skips a missing one and keeps its file paths.</remarks>
    private bool IsMounted(DirectoryInfo root)
    {
        if (root.Exists)
        {
            return true;
        }

        LogRootFolderMissing(root.FullName);
        return false;
    }

    /// <summary>Sends a command that records a library scan, and logs its failure.</summary>
    /// <remarks>The command runs to the end after the scan is cancelled, so a cancelled scan is recorded.</remarks>
    private async Task RecordAsync(object command)
    {
        var result = await runtime.SendCommandAsync(command, CancellationToken.None);
        if (result.IsFailed)
        {
            LogRecordFailed(result.GetFormError());
        }
    }

    /// <summary>Sends a command that changes a video file, and fails the scan when the command fails.</summary>
    private async Task SendAsync(object command, CancellationToken cancellationToken) =>
        ThrowIfFailed(await runtime.SendCommandAsync(command, cancellationToken));

    /// <summary>Fails the scan with a command's failure.</summary>
    private static void ThrowIfFailed(Result result)
    {
        if (result.IsFailed)
        {
            throw new InvalidOperationException(result.GetFormError());
        }
    }

    /// <summary>
    /// Hashes the new and changed files under a folder of an existing root, and removes the file paths it no longer finds,
    /// except those a file scan found since the scan started.
    /// An unchanged file path stores nothing.
    /// </summary>
    private async Task ScanFilesAsync(
        ScanScope scope,
        DirectoryInfo folder,
        DateTimeOffset scanStartedAt,
        ScanTally tally,
        CancellationToken cancellationToken)
    {
        var stored = await ReadStoredUnderAsync(folder, cancellationToken);

        var foundPaths = new HashSet<string>();
        var changedFiles = new List<FileInfo>();
        foreach (var file in EnumerateVideoFiles(scope, folder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foundPaths.Add(file.FullName);
            if (stored.TryGetValue(file.FullName, out var known) && known.Stat.Matches(file))
            {
                tally.AddFound();
            }
            else
            {
                changedFiles.Add(file);
            }
        }

        // A cold read on a network share waits on the far disk, so several hashes in flight keep it busy.
        var unreadPaths = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            changedFiles,
            new ParallelOptions { MaxDegreeOfParallelism = HashParallelism, CancellationToken = cancellationToken },
            async (file, hashCancellationToken) =>
            {
                if (await HashAsync(file, hashCancellationToken) is { } hashed)
                {
                    tally.AddHashed(hashed.VideoFileJoinsLibrary);
                }
                else
                {
                    unreadPaths.Add(file.FullName);
                }
            });

        foundPaths.ExceptWith(unreadPaths);
        var missing = stored.Values.Where(filePath => !foundPaths.Contains(filePath.Id));
        tally.AddDeleted(await RemoveFilePathsAsync(missing, scanStartedAt, cancellationToken));
    }

    /// <summary>
    /// Removes the file paths in batches, one command each, and returns how many it removed.
    /// A path a file scan found since <paramref name="keepFoundSince"/> stays; null keeps none.
    /// </summary>
    public async Task<int> RemoveFilePathsAsync(IEnumerable<StoredFilePath> filePaths, DateTimeOffset? keepFoundSince, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var batch in filePaths.Chunk(BatchSize))
        {
            var removedAt = timeProvider.GetUtcNow();
            List<RemoveFilePath> removals =
            [
                .. batch
                    .Where(filePath => keepFoundSince is not { } since || !(_fileScanFinds.TryGetValue(filePath.Id, out var foundAt) && foundAt >= since))
                    .Select(filePath => new RemoveFilePath(filePath.VideoFile, new LocalPath(filePath.Id), removedAt)),
            ];
            if (removals.Count > 0)
            {
                await SendAsync(new RemoveFilePaths(removals), cancellationToken);
                removed += removals.Count;
            }
        }

        return removed;
    }

    /// <summary>
    /// Archives the video files among <paramref name="videoFiles"/> that have no file path, in batches, one command each,
    /// and returns how many it archived.
    /// </summary>
    public async Task<int> ArchiveAsync(IReadOnlyCollection<FileHash> videoFiles, CancellationToken cancellationToken)
    {
        var archived = 0;
        foreach (var batch in videoFiles.Chunk(BatchSize))
        {
            await SendAsync(new ArchiveVideoFiles(batch, timeProvider.GetUtcNow()), cancellationToken);
            await using var session = store.QuerySession();
            archived += batch.Length - (await session.LoadMediaRowsAsync(batch, cancellationToken)).Count;
        }

        return archived;
    }

    /// <summary>
    /// Hashes a file and stores its file path under the video file its hash identifies, removing it from the video file it held before,
    /// or returns null when the file cannot be read.
    /// A video file found archived, before or during the add, is unarchived and restored with the path.
    /// </summary>
    private async Task<HashedFile?> HashAsync(FileInfo file, CancellationToken cancellationToken)
    {
        FileHash fileHash;
        try
        {
            fileHash = await FileHash.ComputeAsync(file, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFileUnreadable(exception, file.FullName);
            return null;
        }

        var hashedAt = timeProvider.GetUtcNow();
        var path = new LocalPath(file.FullName);
        using (await _videoFileLocks.AcquireAsync(fileHash, cancellationToken))
        {
            FileHash? stored;
            bool archived;
            bool videoFileJoinsLibrary;
            await using (var session = store.QuerySession())
            {
                stored = (await session.LoadAsync<StoredFilePath>(file.FullName, cancellationToken))?.VideoFile;
                var streamState = await session.Events.FetchStreamStateAsync(fileHash.StreamId, cancellationToken);
                archived = streamState is { IsArchived: true };
                videoFileJoinsLibrary = streamState is null || archived;
            }

            // A path belongs to one video file at a time, so new content leaves the old video file before it joins the new one.
            if (stored is { } previous && previous != fileHash)
            {
                await SendAsync(new RemoveFilePath(previous, path, hashedAt), cancellationToken);
            }

            if (archived)
            {
                await SendAsync(new UnarchiveVideoFile(fileHash), cancellationToken);
            }

            var addFilePath = new AddFilePath(fileHash, path, FileStat.From(file), hashedAt);
            var added = await runtime.SendCommandAsync(addFilePath, cancellationToken);
            if (added.HasException<ArchivedStreamException>())
            {
                // A file scan runs beside a library scan's archive and a root folder removal, which can archive the video file after the stream state was read.
                await SendAsync(new UnarchiveVideoFile(fileHash), cancellationToken);
                await SendAsync(addFilePath, cancellationToken);
                videoFileJoinsLibrary = true;
            }
            else
            {
                ThrowIfFailed(added);
            }

            return new HashedFile(fileHash, videoFileJoinsLibrary);
        }
    }

    /// <summary>The file paths under a folder with their video file's hash and the stat they were hashed at, keyed by path.</summary>
    private async Task<Dictionary<string, StoredFilePath>> ReadStoredUnderAsync(DirectoryInfo folder, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return (await session.ReadStoredUnderAsync(folder.FullName, cancellationToken)).ToDictionary(filePath => filePath.Id);
    }

    /// <summary>The file paths under none of the scope's root folders.</summary>
    private async Task<IReadOnlyList<StoredFilePath>> ReadStoredOutsideAsync(ScanScope scope, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        return await session.ReadStoredOutsideAsync(scope.RootFolders.Select(root => root.FullName), cancellationToken);
    }

    /// <summary>Adds the scan to the running scans once no scan pause holds it, with a token the caller's token or a scan pause cancels.</summary>
    private async Task<RunningScan> StartScanAsync(string? folder, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task pauseEnded;
            lock (_lock)
            {
                if (!_paused)
                {
                    var running = new RunningScan(folder, cancellationToken, LeaveRunningScans);
                    _runningScans.Add(running);
                    return running;
                }

                pauseEnded = _pauseEnded.Task;
            }

            await pauseEnded.WaitAsync(cancellationToken);
        }
    }

    private void LeaveRunningScans(RunningScan scan)
    {
        lock (_lock)
        {
            _runningScans.Remove(scan);
        }
    }

    /// <summary>Clears the paused flag, starts the scans the pause held, and ends the pause.</summary>
    private void ResumeScans()
    {
        TaskCompletionSource pauseEnded;
        lock (_lock)
        {
            _paused = false;
            pauseEnded = _pauseEnded;
        }

        pauseEnded.TrySetResult();
        _pauseLock.Release();
    }

    /// <summary>Drops what file scans found before a scan that starts now, which no running or later scan reads.</summary>
    private void ForgetFileScansBefore(DateTimeOffset scanStartedAt)
    {
        foreach (var found in _fileScanFinds)
        {
            if (found.Value < scanStartedAt)
            {
                _fileScanFinds.TryRemove(found);
            }
        }
    }

    private async Task<ScanScope> ReadScanScopeAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var library = await Library.ReadAsync(session, cancellationToken);

        return new ScanScope(
            [.. library.RootFolders.Where(root => root.Enabled).Select(root => new DirectoryInfo(root.Path.Value))],
            library.Settings.VideoExtensions);
    }

    private static IEnumerable<FileInfo> EnumerateVideoFiles(ScanScope scope, DirectoryInfo folder) =>
        folder.Exists
            ? folder.EnumerateFiles("*", EnumerationOptions).Where(scope.VideoExtensions.Includes)
            : [];

    private static IObservable<ActivityEvent> CreateSchedulerEvents(ISchedulerFactory schedulerFactory) =>
        Observable
            .FromAsync(cancellationToken => schedulerFactory.GetScheduler(cancellationToken).AsTask())
            .SelectMany(scheduler => scheduler
                .ObserveJobs(GroupMatcher<JobKey>.GroupEquals(ExecutionGroup))
                .Select(ActivityEvent (jobEvent) => jobEvent is JobFinishedEvent finished
                    // Quartz wraps the scan's exception in exceptions of its own, and the job stores the scan's outcome as its result.
                    ? new ScanFinishedEvent(
                        GetFolder(finished.Context),
                        finished.Context.JobRunTime,
                        finished.Exception?.GetBaseException().Message,
                        finished.Context.Result is ScanOutcome.Cancelled)
                    : new ScanStartedEvent(GetFolder(jobEvent.Context)))
                .Merge(scheduler.ObserveSchedule(Matchers.Key(LibraryScanJob.ScheduledTriggerKey))));

    /// <summary>The folder of a folder scan, which its trigger's key names, and null for a library scan.</summary>
    private static string? GetFolder(IJobExecutionContext context) =>
        context.JobDetail.Key.Equals(FolderScanJob.Key) ? context.Trigger.Key.Name : null;

    /// <summary>A running library or folder scan: the token that cancels it, and its end.</summary>
    /// <param name="folder">The folder of a folder scan; null for a library scan.</param>
    /// <param name="leave">Takes the scan out of the running scans when it ends.</param>
    private sealed class RunningScan(string? folder, CancellationToken callerToken, Action<RunningScan> leave) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? Folder => folder;

        /// <summary>Cancelled by the caller's token or by a scan pause.</summary>
        public CancellationToken Token => _cancellation.Token;

        public Task Ended => _ended.Task;

        /// <summary>Cancels the scan for a scan pause.</summary>
        public void Stop() => _cancellation.Cancel();

        public void Dispose()
        {
            leave(this);
            _ended.TrySetResult();
        }
    }

    /// <summary>A lock per file hash, held while a scan stores a path under that hash, and dropped once no scan holds or waits for it.</summary>
    private sealed class VideoFileLocks
    {
        private readonly Dictionary<FileHash, (SemaphoreSlim Lock, int Users)> _locks = [];

        public async Task<IDisposable> AcquireAsync(FileHash fileHash, CancellationToken cancellationToken)
        {
            SemaphoreSlim semaphore;
            lock (_locks)
            {
                semaphore = _locks.TryGetValue(fileHash, out var entry) ? entry.Lock : new SemaphoreSlim(1, 1);
                _locks[fileHash] = (semaphore, entry.Users + 1);
            }

            try
            {
                await semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                Leave(fileHash);
                throw;
            }

            return Disposable.Create(() =>
            {
                semaphore.Release();
                Leave(fileHash);
            });
        }

        private void Leave(FileHash fileHash)
        {
            lock (_locks)
            {
                var (semaphore, users) = _locks[fileHash];
                if (users == 1)
                {
                    _locks.Remove(fileHash);
                }
                else
                {
                    _locks[fileHash] = (semaphore, users - 1);
                }
            }
        }
    }

    /// <summary>The counts one scan gathers as it runs, from several hashes at once.</summary>
    private sealed class ScanTally
    {
        private int _filePathsFound;
        private int _filesHashed;
        private int _videoFilesAdded;
        private int _filePathsDeleted;

        public int FilePathsFound => Volatile.Read(ref _filePathsFound);

        public int FilesHashed => Volatile.Read(ref _filesHashed);

        public int VideoFilesAdded => Volatile.Read(ref _videoFilesAdded);

        public int FilePathsDeleted => Volatile.Read(ref _filePathsDeleted);

        public void AddFound() => Interlocked.Increment(ref _filePathsFound);

        public void AddHashed(bool videoFileJoinsLibrary)
        {
            Interlocked.Increment(ref _filePathsFound);
            Interlocked.Increment(ref _filesHashed);
            if (videoFileJoinsLibrary)
            {
                Interlocked.Increment(ref _videoFilesAdded);
            }
        }

        public void AddDeleted(int count) => Interlocked.Add(ref _filePathsDeleted, count);
    }

    /// <summary>The video file a hashed file's path was stored under, and whether that video file is new to the library or restored to it.</summary>
    private sealed record HashedFile(FileHash VideoFile, bool VideoFileJoinsLibrary);

    /// <summary>The library settings and enabled roots one scan reads when it starts.</summary>
    private sealed record ScanScope(IReadOnlyList<DirectoryInfo> RootFolders, VideoExtensions VideoExtensions);

    [LoggerMessage(LogLevel.Warning, "Skipped the root folder {RootFolder}, which is missing.")]
    private partial void LogRootFolderMissing(string rootFolder);

    [LoggerMessage(LogLevel.Error, "Could not record the scan. {Error}")]
    private partial void LogRecordFailed(string? error);

    [LoggerMessage(LogLevel.Warning, "Skipped {Path}, which could not be read.")]
    private partial void LogFileUnreadable(Exception exception, string path);
}
