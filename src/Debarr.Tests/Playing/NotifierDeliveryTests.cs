using Debarr.Playing;
using Fisher.Linq;

namespace Debarr.Tests.Playing;

public sealed class NotifierDeliveryTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    [Fact]
    public async Task Each_finished_delivery_is_a_document_with_its_playback_and_everything_it_recorded()
    {
        var first = TestPlayback.Handled(occurredAt: Now);
        var second = TestPlayback.Handled(occurredAt: Now.AddMinutes(1));
        var failed = new Delivery(Guid.NewGuid(), TestPlayback.LightsId, "lights", Now, TimeSpan.FromMilliseconds(1234), new DeliveryOutcome.Failed("Connection refused."));
        var succeeded = new Delivery(Guid.NewGuid(), TestPlayback.AutomationId, "automation", Now, TimeSpan.FromMilliseconds(56), new DeliveryOutcome.Succeeded());
        var cancelled = new Delivery(Guid.NewGuid(), TestPlayback.AutomationId, "automation", Now.AddMinutes(1), TimeSpan.FromMilliseconds(7), new DeliveryOutcome.Cancelled());

        await TestPlayback.RecordAsync(Store, first, [failed, succeeded], CancellationToken);
        await TestPlayback.RecordAsync(Store, second, [cancelled], CancellationToken);

        Assert.Equal(
            [(first.PlaybackId, failed, true), (first.PlaybackId, succeeded, true), (second.PlaybackId, cancelled, false)],
            await ReadDocumentsAsync(failed.Id, succeeded.Id, cancelled.Id));
    }

    [Fact]
    public async Task A_rebuild_replays_every_delivery()
    {
        var playback = TestPlayback.Handled(occurredAt: Now);
        var delivery = TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("Refused."), Now);
        await TestPlayback.RecordAsync(Store, playback, [delivery], CancellationToken);
        await using (var session = Store.LightweightSession())
        {
            var document = await session.LoadAsync<NotifierDelivery>(delivery.Id, CancellationToken);
            document!.Outcome = new DeliveryOutcome.Failed("Changed.");
            session.Store(document);
            await session.SaveChangesAsync(CancellationToken);
        }

        await RebuildAsync(NotifierDeliveryProjection.ReadModel);

        await using var query = Store.QuerySession();
        Assert.Equal([(playback.PlaybackId, delivery, true)], await ReadDocumentsAsync(delivery.Id));
        Assert.Equal(1, await query.Query<NotifierDelivery>().CountAsync(CancellationToken));
    }

    private async Task<List<(Guid PlaybackId, Delivery Delivery, bool RanToEnd)>> ReadDocumentsAsync(params Guid[] deliveryIds)
    {
        await using var session = Store.QuerySession();
        List<(Guid, Delivery, bool)> documents = [];
        foreach (var deliveryId in deliveryIds)
        {
            var document = Assert.IsType<NotifierDelivery>(await session.LoadAsync<NotifierDelivery>(deliveryId, CancellationToken));
            documents.Add((document.PlaybackId, document.ToDelivery(), document.RanToEnd));
        }

        return documents;
    }
}
