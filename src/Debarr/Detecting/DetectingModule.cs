using Debarr.Activity;
using Debarr.EventStore;
using Debarr.Health;
using Fisher;

namespace Debarr.Detecting;

public static class DetectingModule
{
    public static IServiceCollection AddDetecting(this IServiceCollection services)
    {
        services.AddSingleton<FfprobeRunner>();
        services.AddSingleton<CropDetectRunner>();
        services.AddSingleton<FfmpegVersions>();
        services.AddSingleton<IAspectRatioDetector, AspectRatioDetector>();
        services.AddSingleton<DetectionRunner>();
        services.AddSingleton<DetectionOrchestrator>();
        services.AddSingleton<IActivitySource>(provider => provider.GetRequiredService<DetectionOrchestrator>());
        services.AddSingleton<IHealthCheck, FfmpegHealthCheck>();
        services.AddSingleton<IHealthCheck, DetectionHealthCheck>();
        services.AddFoldedAggregate<VideoFile>();
        services.AddFoldedAggregate<DetectionSettings>();
        return services.ConfigureFisher(MediaRowProjection.AddTo);
    }
}
