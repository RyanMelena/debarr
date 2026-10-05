using Debarr.Playing;
using Debarr.Tests.Detecting;
using Debarr.Tests.Extensions;
using Fisher.Linq;

namespace Debarr.Tests.Playing;

public sealed class HistoryClearTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    [Fact]
    public async Task A_clear_hides_every_playback_that_came_before_it_and_every_delivery_that_started_before_it_from_every_read()
    {
        var film = TestFileHash.For("film");
        var before = TestPlayback.Handled(title: "Before", videoFile: film, occurredAt: Now.AddMinutes(-1), playerId: TestPlayback.TheaterId);
        await TestPlayback.RecordAsync(
            Store,
            TestPlayback.Handled("Bedroom", title: "Other player", occurredAt: Now.AddMinutes(-1), playerId: TestPlayback.BedroomId),
            [TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("Refused."), Now.AddMinutes(-1))],
            CancellationToken);
        await TestPlayback.RecordAsync(Store, [before], CancellationToken);

        await ClearAsync(Now);
        await AppendAsync(before.PlaybackId, new DeliveryFinished(before.PlaybackId, TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("Late."), Now.AddSeconds(30))));
        var after = TestPlayback.Handled(title: "After", videoFile: film, occurredAt: Now.AddMinutes(1), playerId: TestPlayback.TheaterId);
        await TestPlayback.RecordAsync(Store, after, [TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Succeeded(), startedAt: Now.AddMinutes(1))], CancellationToken);

        await using var session = Store.QuerySession();
        var all = new PlaybackRowView(null, null, null, PlaybackRowSort.Time, true);
        Assert.Equal(1, await session.CountShownPlaybackRowsAsync(CancellationToken));
        var counts = await session.CountPlaybackRowsByOutcomeAsync(all.Scope, CancellationToken);
        Assert.Equal(new PlaybackRowCounts(1, 1, 0, 0), counts);
        Assert.Equal(["Theater"], await session.ReadPlayerNamesAsync(CancellationToken));
        Assert.Equal(["After"], (await session.ReadHistoryPageAsync(all, counts, 0, 50, CancellationToken)).Select(row => row.Title));
        Assert.Equal(
            [(TestPlayback.TheaterId, after.PlaybackId)],
            (await session.ReadLastPlaybacksAsync([TestPlayback.TheaterId, TestPlayback.BedroomId], CancellationToken)).Select(lastPlayback => (lastPlayback.Key, lastPlayback.Value.Id)));
        Assert.Equal([new DeliveryOutcome.Succeeded(), new DeliveryOutcome.Failed("Late.")], (await session.ReadNewestDeliveriesAsync(TestPlayback.AutomationId, 5, CancellationToken)).Select(delivery => delivery.Outcome));
        Assert.Equal([new DeliveryOutcome.Succeeded(), new DeliveryOutcome.Failed("Late.")], (await session.ReadNewestDeliveriesThatRanToEndAsync(TestPlayback.AutomationId, 5, CancellationToken)).Select(delivery => delivery.Outcome));
        Assert.Equal(["After"], (await session.ReadVideoFilePlaybacksAsync(film, CancellationToken)).Select(row => row.Title));
        Assert.Equal(3, await session.Query<PlaybackRow>().CountAsync(CancellationToken));
    }

    [Fact]
    public async Task A_rebuild_keeps_the_newest_clear()
    {
        await TestPlayback.RecordAsync(Store, [TestPlayback.Handled(title: "Before", occurredAt: Now.AddMinutes(-1))], CancellationToken);
        await ClearAsync(Now.AddMinutes(-2));
        await ClearAsync(Now);
        await using (var session = Store.LightweightSession())
        {
            session.Store(new HistoryClear { Id = ClearHistoryHandler.HistoryStreamId, ClearedAt = Now.AddDays(-1) });
            await session.SaveChangesAsync(CancellationToken);
        }

        await RebuildAsync(HistoryClearProjection.ReadModel);

        await using var query = Store.QuerySession();
        Assert.Equal(Now, (await query.LoadAsync<HistoryClear>(ClearHistoryHandler.HistoryStreamId, CancellationToken))?.ClearedAt);
        Assert.Equal(0, await query.CountShownPlaybackRowsAsync(CancellationToken));
    }

    private Task ClearAsync(DateTimeOffset clearedAt) => AppendAsync(ClearHistoryHandler.HistoryStreamId, new HistoryCleared(clearedAt));

    private async Task AppendAsync(Guid streamId, object @event)
    {
        await using var session = Store.LightweightSession();
        await session.Events.AppendAtCurrentVersionAsync(streamId, @event);
        await session.SaveChangesAsync(CancellationToken);
    }
}
