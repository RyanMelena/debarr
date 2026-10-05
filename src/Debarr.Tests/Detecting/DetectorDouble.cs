using System.Collections.Concurrent;
using Debarr.Detecting;
using FluentResults;

namespace Debarr.Tests.Detecting;

/// <summary>An aspect ratio detector that records the paths it detects and answers with <see cref="Respond"/>, holding each detection until its release while <see cref="Holds"/> is set.</summary>
public sealed class DetectorDouble : IAspectRatioDetector
{
    public static readonly ContainerMetadata Hevc1080 = new(new AspectRatio(1920.0 / 1080), 1920, 1080, "hevc", "bt709");

    public static readonly DetectorOutcome Letterboxed240 = new(
        "8.1.2",
        Hevc1080,
        new DetectionResult(AspectRatioSource.Detected, new AspectRatio(2.4), 1.0, [new CropSample(TimeSpan.FromSeconds(30), new CropBox(1920, 800))]));

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();
    private readonly ConcurrentQueue<string> _paths = new();
    private int _running;
    private int _cancelled;

    public bool Holds { get; set; }

    /// <summary>Holds a detection until its release even after its cancellation.</summary>
    public bool IgnoresCancellation { get; set; }

    public Func<string, DetectorOutcome> Respond { get; set; } = _ => Letterboxed240;

    /// <summary>How many held detections had their cancellation requested.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    public IReadOnlyCollection<string> Paths => [.. _paths];

    public int Running => Volatile.Read(ref _running);

    /// <summary>A failure after ffprobe read <see cref="Hevc1080"/>.</summary>
    public static DetectorOutcome Failed(string error) => Letterboxed240 with { Result = Result.Fail(error) };

    public void Release(string path) => GetGate(path).TrySetResult();

    public void ReleaseAll()
    {
        Holds = false;
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }
    }

    public async Task<DetectorOutcome> DetectAsync(
        string path,
        DetectionSettings detectionSettings,
        CancellationToken cancellationToken)
    {
        _paths.Enqueue(path);
        Interlocked.Increment(ref _running);
        try
        {
            if (Holds && IgnoresCancellation)
            {
                await using var registration = cancellationToken.Register(() => Interlocked.Increment(ref _cancelled));
                await GetGate(path).Task;
            }
            else if (Holds)
            {
                await GetGate(path).Task.WaitAsync(cancellationToken);
            }

            return Respond(path);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private TaskCompletionSource GetGate(string path) =>
        _gates.GetOrAdd(path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
