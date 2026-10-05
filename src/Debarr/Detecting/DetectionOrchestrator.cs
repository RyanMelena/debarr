using System.Reactive.Linq;
using System.Reactive.Subjects;
using Debarr.Activity;
using Fisher;
using FluentResults;

namespace Debarr.Detecting;

/// <summary>
/// Starts every detection and holds the running ones: the detection queue's at each queue check, up to the simultaneous detections,
/// and Detect Now's at once, one at a time in a slot of its own. One detection runs per video file.
/// A detection pause cancels the running detections and starts none until it ends.
/// </summary>
public sealed partial class DetectionOrchestrator(
    IDocumentStore store,
    DetectionRunner runner,
    TimeProvider timeProvider,
    ILogger<DetectionOrchestrator> logger) : IHostedService, IActivitySource
{
    /// <summary>How often the detection queue is checked for free slots.</summary>
    public static readonly TimeSpan QueueCheckInterval = TimeSpan.FromSeconds(1);

    private static readonly Func<ILogger, FileHash, IDisposable?> DetectionScope = LoggerMessage.DefineScope<FileHash>("Detection of {VideoFile}");

    // Guards the running list and the paused flag, so a start never races a pause.
    private readonly Lock _lock = new();

    private readonly Dictionary<FileHash, TrackedDetection> _running = [];

    // Detections end on their own threads, so the subject serializes their events.
    private readonly ISubject<ActivityEvent> _subject = Subject.Synchronize(new Subject<ActivityEvent>());

    // One detection pause at a time.
    private readonly SemaphoreSlim _pauseLock = new(1, 1);

    // Cancelled when the host stops, which ends the queue checks.
    private readonly CancellationTokenSource _stopping = new();

    private bool _paused;

    private Task _queueChecks = Task.CompletedTask;

    /// <summary>A <see cref="DetectionStartedEvent"/> and a <see cref="DetectionFinishedEvent"/> for each detection.</summary>
    public IObservable<ActivityEvent> ActivityEvents => _subject.AsObservable();

    public IReadOnlyList<RunningDetection> RunningDetections
    {
        get
        {
            lock (_lock)
            {
                return [.. _running.Values.Select(running => running.RunningDetection)];
            }
        }
    }

    public static bool ChangesRunningDetections(ActivityEvent activityEvent) => activityEvent is DetectionStartedEvent or DetectionFinishedEvent;

    /// <summary>Checks the detection queue now, and then once every <see cref="QueueCheckInterval"/>.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _queueChecks = RunQueueChecksAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <summary>Ends the queue checks, cancels every running detection and waits for them to end.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await _queueChecks;
        await PauseAndCancelAllAsync();
    }

    /// <summary>
    /// Starts a detection pause: cancels every running detection, waits for them to end, and starts none until the pause is disposed.
    /// A cancelled detection writes nothing. After the pause, the next queue check starts queued detections.
    /// </summary>
    public async Task<DetectionPause> PauseDetectionsAsync(CancellationToken cancellationToken)
    {
        await _pauseLock.WaitAsync(cancellationToken);
        try
        {
            await PauseAndCancelAllAsync().WaitAsync(cancellationToken);
        }
        catch
        {
            Resume();
            throw;
        }

        return new DetectionPause(Resume);
    }

    /// <summary>
    /// Detects the video file at once, beside the detection queue.
    /// Fails during a detection pause, while another Detect Now runs, while the video file is being detected, or when it has no file path.
    /// </summary>
    public async Task<Result> DetectNowAsync(FileHash videoFile, CancellationToken cancellationToken)
    {
        DetectionRequest? request;
        await using (var session = store.QuerySession())
        {
            request = (await VideoFile.ReadAsync(session, videoFile, cancellationToken))?.ToDetectionRequest(DetectionOrigin.DetectNow);
        }

        return request is null
            ? Result.Fail("The video file has no file path to detect.")
            : TryStart(request);
    }

    /// <summary>Runs a queue check now and at each tick, one at a time, until the host stops.</summary>
    private async Task RunQueueChecksAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(QueueCheckInterval, timeProvider);
        try
        {
            do
            {
                await FillFreeDetectionSlotsFromDetectionQueueAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Starts the newest video files in the detection queue that fit in the free detection slots.</summary>
    private async Task FillFreeDetectionSlotsFromDetectionQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = store.QuerySession();
            var simultaneousDetections = (await DetectionSettings.ReadAsync(session, cancellationToken)).SimultaneousDetections;

            int freeDetectionSlots;
            List<FileHash> runningVideoFiles;
            lock (_lock)
            {
                freeDetectionSlots = _paused ? 0 : simultaneousDetections - _running.Values.Count(running => running.RunningDetection.Origin == DetectionOrigin.Queue);
                runningVideoFiles = [.. _running.Keys];
            }

            if (freeDetectionSlots <= 0)
            {
                return;
            }

            var requests = await session.ReadDetectionQueueAsync(freeDetectionSlots, runningVideoFiles, cancellationToken);

            // Detect Now can take a requested video file while the query runs, and the next queue check fills the slot it leaves.
            foreach (var request in requests)
            {
                TryStart(request);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogQueueCheckFailed(exception);
        }
    }

    /// <summary>
    /// Starts the requested detection, and fails during a detection pause, for a Detect Now while another Detect Now runs,
    /// and while another detection of its video file is running.
    /// </summary>
    private Result TryStart(DetectionRequest request)
    {
        Task<Task> detection;
        lock (_lock)
        {
            if (_paused)
            {
                return Result.Fail("Detection is paused. It resumes on its own, so try again shortly.");
            }

            if (request.Origin == DetectionOrigin.DetectNow
                && _running.Values.Any(running => running.RunningDetection.Origin == DetectionOrigin.DetectNow))
            {
                return Result.Fail("Detect Now is already running on another video file.");
            }

            if (_running.ContainsKey(request.VideoFile))
            {
                return Result.Fail("The video file is already being detected.");
            }

            var runningDetection = new RunningDetection(request.VideoFile, request.Path, request.Origin, timeProvider.GetUtcNow());
            var cancellation = new CancellationTokenSource();
            detection = new Task<Task>(() => RunTrackedAsync(runningDetection, cancellation));
            _running.Add(request.VideoFile, new TrackedDetection(runningDetection, detection.Unwrap(), cancellation));
        }

        // Started only once its entry is in the running list, since the task publishes its events and removes that entry.
        detection.Start(TaskScheduler.Default);
        return Result.Ok();
    }

    /// <summary>Runs one detection while it is in the running list, between its started and finished events, and removes it from the list however it ends.</summary>
    private async Task RunTrackedAsync(RunningDetection runningDetection, CancellationTokenSource cancellation)
    {
        var videoFile = runningDetection.VideoFile;
        _subject.OnNext(new DetectionStartedEvent(videoFile, runningDetection.Path));
        using var scope = DetectionScope(logger, videoFile);
        LogDetectionStarted(runningDetection.Path.Value);
        try
        {
            await runner.RunAsync(runningDetection, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            LogDetectionCancelled(runningDetection.Path.Value);
        }
        catch (Exception exception)
        {
            LogDetectionFailed(exception, videoFile);
        }
        finally
        {
            lock (_lock)
            {
                _running.Remove(videoFile);
            }

            _subject.OnNext(new DetectionFinishedEvent(videoFile, runningDetection.Path));
        }
    }

    /// <summary>Sets the paused flag, so nothing starts, then cancels every running detection and completes when each has ended.</summary>
    private async Task PauseAndCancelAllAsync()
    {
        List<TrackedDetection> running;
        lock (_lock)
        {
            _paused = true;
            running = [.. _running.Values];
        }

        foreach (var detection in running)
        {
            await detection.Cancellation.CancelAsync();
        }

        await Task.WhenAll(running.Select(detection => detection.Task));
    }

    /// <summary>Clears the paused flag and ends the pause.</summary>
    private void Resume()
    {
        lock (_lock)
        {
            _paused = false;
        }

        _pauseLock.Release();
    }

    /// <summary>An entry in the running list: a running detection, the task that runs it, and the source that cancels it.</summary>
    private sealed record TrackedDetection(RunningDetection RunningDetection, Task Task, CancellationTokenSource Cancellation);

    [LoggerMessage(LogLevel.Debug, "Started detecting {Path}.")]
    private partial void LogDetectionStarted(string path);

    [LoggerMessage(LogLevel.Debug, "Cancelled the detection of {Path}.")]
    private partial void LogDetectionCancelled(string path);

    [LoggerMessage(LogLevel.Error, "Checking the detection queue failed.")]
    private partial void LogQueueCheckFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "The detection of video file {VideoFile} failed.")]
    private partial void LogDetectionFailed(Exception exception, FileHash videoFile);
}
