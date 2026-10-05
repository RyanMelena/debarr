using Debarr.EventStore;
using Debarr.Health;
using MQTTnet;

namespace Debarr.Notifying;

public static class NotifyingModule
{
    public static IServiceCollection AddNotifying(this IServiceCollection services)
    {
        // NotificationPublisher's timeout is the delivery budget.
        services.AddHttpClient(WebhookNotifierClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<MqttClientFactory>();
        services.AddSingleton<NotificationPublisher>();
        services.AddSingleton<IHealthCheck, NotifierHealthCheck>();
        return services.AddFoldedAggregate<Notifiers>();
    }
}
