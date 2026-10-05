using System.Reactive;
using System.Reactive.Linq;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Health;
using Fisher;

namespace Debarr.Detecting;

/// <summary>Counts the video files that failed detection, as Media's failed filter counts them.</summary>
public sealed class DetectionHealthCheck(IDocumentStore store, ReadModelChangeListener readModelChangeListener) : IHealthCheck
{
    public HealthCheckKind Kind => HealthCheckKind.Detection;

    public IObservable<Unit> Triggers { get; } = readModelChangeListener.ChangesTo(MediaRowProjection.ReadModel).Select(_ => Unit.Default);

    public async Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var failedCount = (await session.CountMediaRowsByStatusAsync(null, cancellationToken)).GetValueOrDefault(VideoFileStatus.Failed);

        return failedCount == 0
            ? []
            : [new HealthMessage(HealthSeverity.Warning, $"{failedCount.ToCountText("file", "files")} failed detection. A playback of one sends the player's ratio.", "?status=failed", "Show them in Media")];
    }
}
