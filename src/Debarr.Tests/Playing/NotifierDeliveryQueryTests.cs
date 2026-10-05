using Debarr.Playing;
using Debarr.Tests.Extensions;
using Fisher.Linq;

namespace Debarr.Tests.Playing;

public sealed class NotifierDeliveryQueryTests : AppTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 20, 15, 0, 123, TimeSpan.Zero);

    [Fact]
    public async Task A_notifiers_newest_deliveries_come_newest_first_up_to_the_count_and_a_run_to_the_end_skips_a_cancelled_one()
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await RecordAsync(
                Now.AddMinutes(attempt),
                TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed($"Attempt {attempt}"), Now.AddMinutes(attempt)),
                TestPlayback.Delivery(TestPlayback.LightsId, "lights", new DeliveryOutcome.Failed("Lights"), Now.AddMinutes(attempt)));
        }

        await RecordAsync(Now.AddMinutes(4), TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Cancelled(), startedAt: Now.AddMinutes(4)));
        await RecordAsync(Now.AddMinutes(5), TestPlayback.Delivery(TestPlayback.LightsId, "lights", new DeliveryOutcome.Succeeded(), startedAt: Now.AddMinutes(5)));

        Assert.Equal([null, "Attempt 3"], await ReadErrorsAsync(TestPlayback.AutomationId, 2, ranToEnd: false));
        Assert.Equal(["Attempt 3", "Attempt 2"], await ReadErrorsAsync(TestPlayback.AutomationId, 2, ranToEnd: true));
        Assert.Equal([null, "Attempt 3", "Attempt 2", "Attempt 1"], await ReadErrorsAsync(TestPlayback.AutomationId, 10, ranToEnd: false));
        Assert.Equal([null, "Lights"], await ReadErrorsAsync(TestPlayback.LightsId, 2, ranToEnd: true));
    }

    [Fact]
    public async Task A_notifier_with_no_deliveries_has_no_newest()
    {
        await RecordAsync(Now, TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Succeeded(), startedAt: Now));

        Assert.Empty(await ReadErrorsAsync(TestPlayback.LightsId, 5, ranToEnd: false));
        Assert.Empty(await ReadErrorsAsync(TestPlayback.LightsId, 5, ranToEnd: true));
    }

    [Fact]
    public async Task After_a_clear_only_the_deliveries_that_started_after_it_are_the_newest()
    {
        await RecordAsync(Now.AddMinutes(-2), TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("Before."), Now.AddMinutes(-2)));
        await RecordAsync(Now.AddMinutes(-1), TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("At the clear."), Now));
        await using (var session = Store.LightweightSession())
        {
            await session.Events.AppendAtCurrentVersionAsync(ClearHistoryHandler.HistoryStreamId, new HistoryCleared(Now));
            await session.SaveChangesAsync(CancellationToken);
        }

        await RecordAsync(Now.AddMinutes(1), TestPlayback.Delivery(TestPlayback.AutomationId, "automation", new DeliveryOutcome.Failed("After."), Now.AddMinutes(1)));

        Assert.Equal(["After."], await ReadErrorsAsync(TestPlayback.AutomationId, 5, ranToEnd: false));
        Assert.Equal(["After."], await ReadErrorsAsync(TestPlayback.AutomationId, 5, ranToEnd: true));
    }

    [Theory]
    [InlineData(false, "idx_fi_doc_notifierdelivery_notifier_id_started_at")]
    [InlineData(true, "idx_fi_doc_notifierdelivery_notifier_id_ran_to_end_started_at")]
    public async Task A_notifiers_newest_deliveries_read_its_index_in_order(bool ranToEnd, string index)
    {
        await using var session = Store.QuerySession();
        var shown = await session.ShownNotifierDeliveriesAsync(CancellationToken);

        var newest = ranToEnd ? shown.NewestThatRanToEndBy(TestPlayback.AutomationId) : shown.NewestBy(TestPlayback.AutomationId);
        var plan = await newest.Take(5).ExplainAsync(CancellationToken);

        Assert.True(
            plan.Steps.Any(step => step.Detail.StartsWith($"SEARCH fi_doc_notifierdelivery USING INDEX {index} ", StringComparison.Ordinal))
            && !plan.Steps.Any(step => step.Detail.Contains("TEMP B-TREE", StringComparison.Ordinal)),
            plan.ToString());
    }

    private Task RecordAsync(DateTimeOffset occurredAt, params Delivery[] deliveries) =>
        TestPlayback.RecordAsync(Store, TestPlayback.Handled(occurredAt: occurredAt), deliveries, CancellationToken);

    private async Task<List<string?>> ReadErrorsAsync(Guid notifierId, int count, bool ranToEnd)
    {
        await using var session = Store.QuerySession();
        var deliveries = ranToEnd
            ? await session.ReadNewestDeliveriesThatRanToEndAsync(notifierId, count, CancellationToken)
            : await session.ReadNewestDeliveriesAsync(notifierId, count, CancellationToken);
        return [.. deliveries.Select(delivery => (delivery.Outcome as DeliveryOutcome.Failed)?.Error)];
    }
}
