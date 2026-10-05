using Debarr.EventStore;
using Debarr.Scanning;
using Fisher;
using Wolverine.Fisher;

namespace Debarr.Detecting;

/// <summary>Clears every video file's current result and last failure, keeping overrides and detections, so the whole library rejoins the detection queue.</summary>
public sealed record RedetectAll;

public static class RedetectAllHandler
{
    /// <summary>
    /// Starts a library write pause: it stops the running scan and pauses scans, since a scan that writes to a video file the command clears would fail its attempt,
    /// then pauses detections, since a running one would record a result the command clears.
    /// Wolverine runs it inside the command's log scope.
    /// </summary>
    public static Task<LibraryWritePause> BeforeAsync(RedetectAll command, LibraryScanner scanner, DetectionOrchestrator orchestrator, CancellationToken cancellationToken) =>
        LibraryWritePause.StartAsync(scanner, orchestrator, cancellationToken);

    /// <summary>
    /// Ends the pause <see cref="BeforeAsync"/> returned, from the generated handler's <c>finally</c> block when the command finishes.
    /// The generated handler also holds the pause in a <c>using</c>, which disposes it again afterwards, and the pause ends once.
    /// </summary>
    public static void Finally(LibraryWritePause pause) => pause.Dispose();

    /// <summary>The streams of the video files with a current result or a last failure.</summary>
    public static Task<IReadOnlyList<StreamVersion>> LoadAsync(RedetectAll command, IQuerySession session, CancellationToken cancellationToken) =>
        session.ReadVersionsWithResultOrFailureAsync(cancellationToken);

    /// <summary>Clears each video file's current result and last failure, at the version its row was read at, in one transaction.</summary>
    public static IEnumerable<IFisherOp> Handle(RedetectAll command, IReadOnlyList<StreamVersion> videoFiles, TimeProvider timeProvider)
    {
        var cleared = new DetectionResultCleared(timeProvider.GetUtcNow());
        return videoFiles.Select(videoFile => FisherOps.Append(videoFile.Id, videoFile.Version, cleared));
    }
}
