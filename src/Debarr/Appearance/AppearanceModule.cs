using Debarr.EventStore;

namespace Debarr.Appearance;

public static class AppearanceModule
{
    public static IServiceCollection AddAppearance(this IServiceCollection services) =>
        services.AddFoldedAggregate<UISettings>();
}
