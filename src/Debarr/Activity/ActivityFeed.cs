using System.Reactive.Linq;

namespace Debarr.Activity;

/// <summary>Merges the events of every activity source into one stream, which the UI's pages and activity messages follow.</summary>
public sealed class ActivityFeed(IEnumerable<IActivitySource> activitySources)
{
    /// <summary>Each subscription receives the events one at a time, and the stream stays open while the app runs.</summary>
    public IObservable<ActivityEvent> Events { get; } = activitySources
        .Select(activitySource => activitySource.ActivityEvents)
        // Keeps the merge open while no activity source is registered.
        .Append(Observable.Never<ActivityEvent>())
        .Merge();
}
