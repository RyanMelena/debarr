using Fisher;
using Fisher.Linq;
using Fisher.Projections;
using JasperFx.Events.Projections;

namespace Debarr.Playing;

public sealed class NotifierDelivery
{
    public Guid Id { get; set; }

    public Guid PlaybackId { get; set; }

    public Guid NotifierId { get; set; }

    public string NotifierName { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    public TimeSpan Duration { get; set; }

    public DeliveryOutcome Outcome { get; set; } = default!;

    public bool RanToEnd => Outcome is not DeliveryOutcome.Cancelled;

    public Delivery ToDelivery() => new(Id, NotifierId, NotifierName, StartedAt, Duration, Outcome);
}

/// <summary>Writes a document per delivery from the playback streams; each event writes the whole document, so two commits at once lose nothing.</summary>
public sealed class NotifierDeliveryProjection : MultiStreamProjection<NotifierDelivery, Guid>
{
    public const string ReadModel = nameof(NotifierDelivery);

    public NotifierDeliveryProjection()
    {
        Name = ReadModel;
        Identity<DeliveryFinished>(finished => finished.Delivery.Id);
    }

    public static void AddTo(StoreOptions options)
    {
        options.Schema.For<NotifierDelivery>()
            .Index([delivery => delivery.NotifierId, delivery => delivery.StartedAt])
            .Index([delivery => delivery.NotifierId, delivery => delivery.RanToEnd, delivery => delivery.StartedAt]);
        options.Projections.Add(new NotifierDeliveryProjection(), ProjectionLifecycle.Inline);
    }

    public static NotifierDelivery Create(DeliveryFinished finished) => new()
    {
        Id = finished.Delivery.Id,
        PlaybackId = finished.PlaybackId,
        NotifierId = finished.Delivery.NotifierId,
        NotifierName = finished.Delivery.NotifierName,
        StartedAt = finished.Delivery.StartedAt,
        Duration = finished.Delivery.Duration,
        Outcome = finished.Delivery.Outcome,
    };
}

public static class NotifierDeliveryQuery
{
    public static async Task<IReadOnlyList<Delivery>> ReadNewestDeliveriesAsync(this IQuerySession session, Guid notifierId, int count, CancellationToken cancellationToken) =>
        ToDeliveries(await (await session.ShownNotifierDeliveriesAsync(cancellationToken)).NewestBy(notifierId).Take(count).ToListAsync(cancellationToken));

    public static async Task<IReadOnlyList<Delivery>> ReadNewestDeliveriesThatRanToEndAsync(this IQuerySession session, Guid notifierId, int count, CancellationToken cancellationToken) =>
        ToDeliveries(await (await session.ShownNotifierDeliveriesAsync(cancellationToken)).NewestThatRanToEndBy(notifierId).Take(count).ToListAsync(cancellationToken));

    public static async Task<IQueryable<NotifierDelivery>> ShownNotifierDeliveriesAsync(this IQuerySession session, CancellationToken cancellationToken)
    {
        var clearedAt = await session.ReadHistoryClearedAtAsync(cancellationToken);
        return session.Query<NotifierDelivery>().Where(delivery => delivery.StartedAt > clearedAt);
    }

    public static IQueryable<NotifierDelivery> NewestBy(this IQueryable<NotifierDelivery> deliveries, Guid notifierId) =>
        deliveries.Where(delivery => delivery.NotifierId == notifierId).OrderByDescending(delivery => delivery.StartedAt);

    public static IQueryable<NotifierDelivery> NewestThatRanToEndBy(this IQueryable<NotifierDelivery> deliveries, Guid notifierId) =>
        deliveries.Where(delivery => delivery.NotifierId == notifierId && delivery.RanToEnd).OrderByDescending(delivery => delivery.StartedAt);

    private static IReadOnlyList<Delivery> ToDeliveries(IEnumerable<NotifierDelivery> deliveries) => [.. deliveries.Select(delivery => delivery.ToDelivery())];
}
