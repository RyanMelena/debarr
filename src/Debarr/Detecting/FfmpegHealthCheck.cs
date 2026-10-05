using System.Reactive;
using System.Reactive.Linq;
using Debarr.Health;

namespace Debarr.Detecting;

/// <summary>Finds an ffmpeg or ffprobe that can't be run or that fails to report its version, once at startup, since their paths apply on restart.</summary>
public sealed class FfmpegHealthCheck(FfmpegVersions ffmpegVersions) : IHealthCheck
{
    public HealthCheckKind Kind => HealthCheckKind.Ffmpeg;

    public IObservable<Unit> Triggers => Observable.Never<Unit>();

    public async Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken)
    {
        var ffmpeg = await ffmpegVersions.GetFfmpegAsync(cancellationToken);
        var ffprobe = await ffmpegVersions.GetFfprobeAsync(cancellationToken);

        return
        [
            .. new[] { ffmpeg, ffprobe }
                .Where(result => result.IsFailed)
                .Select(result => new HealthMessage(
                    HealthSeverity.Error,
                    $"Every detection fails until this is fixed: {string.Join(" ", result.Errors.Select(error => error.Message))}",
                    "settings/general",
                    "Fix in Settings > General")),
        ];
    }
}
