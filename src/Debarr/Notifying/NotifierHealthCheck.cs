using System.Reactive;
using System.Reactive.Linq;
using Debarr.EventStore;
using Debarr.Extensions;
using Debarr.Health;
using Debarr.Playing;
using Fisher;

namespace Debarr.Notifying;

/// <summary>Finds each enabled notifier whose last delivery that ran to its end failed.</summary>
public sealed class NotifierHealthCheck(IDocumentStore store, ReadModelChangeListener readModelChangeListener) : IHealthCheck
{
    public HealthCheckKind Kind => HealthCheckKind.Notifier;

    public IObservable<Unit> Triggers { get; } = readModelChangeListener
        .ChangesTo(nameof(Notifiers), NotifierDeliveryProjection.ReadModel, HistoryClearProjection.ReadModel)
        .Select(_ => Unit.Default);

    public async Task<IReadOnlyList<HealthMessage>> CheckAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var enabled = (await Notifiers.ReadAsync(session, cancellationToken)).All
            .Where(notifier => notifier.Enabled)
            .OrderBy(notifier => notifier.Name, StringComparer.Ordinal);
        List<HealthMessage> messages = [];
        foreach (var notifier in enabled)
        {
            if (await session.ReadNewestDeliveriesThatRanToEndAsync(notifier.Id, 1, cancellationToken) is [{ Outcome: DeliveryOutcome.Failed failed } lastDelivery])
            {
                messages.Add(new HealthMessage(
                    HealthSeverity.Error,
                    $"{notifier.Name} failed to deliver the last notification: {failed.Error}",
                    "settings/notifiers",
                    "Fix in Settings > Notifiers",
                    lastDelivery.StartedAt));
            }
        }

        return messages;
    }
}
