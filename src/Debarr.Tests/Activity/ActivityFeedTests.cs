using System.Reactive.Subjects;
using Debarr.Activity;

namespace Debarr.Tests.Activity;

public class ActivityFeedTests
{
    [Fact]
    public void Events_from_every_activity_source_reach_a_subscriber()
    {
        var first = new Subject<ActivityEvent>();
        var second = new Subject<ActivityEvent>();
        var feed = new ActivityFeed([new TestActivitySource(first), new TestActivitySource(second)]);
        var received = new List<int>();
        using var subscription = feed.Events.Subscribe(activityEvent => received.Add(((NumberedEvent)activityEvent).Number));

        first.OnNext(new NumberedEvent(1));
        second.OnNext(new NumberedEvent(2));
        first.OnNext(new NumberedEvent(3));

        Assert.Equal([1, 2, 3], received);
    }

    [Fact]
    public async Task Concurrent_activity_sources_deliver_one_event_at_a_time()
    {
        var sources = Enumerable.Range(0, 8).Select(_ => new Subject<ActivityEvent>()).ToList();
        var feed = new ActivityFeed(sources.Select(source => new TestActivitySource(source)));
        var inside = 0;
        var mostInside = 0;
        var received = new List<int>();
        using var subscription = feed.Events.Subscribe(activityEvent =>
        {
            mostInside = Math.Max(mostInside, Interlocked.Increment(ref inside));
            received.Add(((NumberedEvent)activityEvent).Number);
            Thread.SpinWait(1_000);
            Interlocked.Decrement(ref inside);
        });

        await Task.WhenAll(sources.Select((source, index) => Task.Run(() =>
        {
            for (var number = 0; number < 100; number++)
            {
                source.OnNext(new NumberedEvent(index * 100 + number));
            }
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(1, mostInside);
        Assert.Equal(Enumerable.Range(0, 800), received.Order());
    }

    [Fact]
    public void The_feed_stays_open_with_no_activity_source()
    {
        var feed = new ActivityFeed([]);
        var terminated = false;
        using var subscription = feed.Events.Subscribe(_ => { }, _ => terminated = true, () => terminated = true);

        Assert.False(terminated);
    }

    private sealed record NumberedEvent(int Number) : ActivityEvent;
}
