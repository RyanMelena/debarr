using Debarr.Activity;
using Debarr.EventStore;
using Debarr.Health;
using Fisher;

namespace Debarr.Playing;

public static class PlayingModule
{
    public static IServiceCollection AddPlaying(this IServiceCollection services)
    {
        services.AddSingleton<PlayerConnectionFactory>();
        services.AddSingleton<PlayerConnectionService>();
        services.AddSingleton<IActivitySource>(provider => provider.GetRequiredService<PlayerConnectionService>());
        services.AddSingleton<PlaybackHandler>();
        services.AddSingleton<IHealthCheck, PlayerConnectionHealthCheck>();
        services.AddFoldedAggregate<Players>();
        return services.ConfigureFisher(options =>
        {
            HistoryClearProjection.AddTo(options);
            NotifierDeliveryProjection.AddTo(options);
            PlaybackRowProjection.AddTo(options);
        });
    }
}
