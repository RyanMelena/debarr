namespace Debarr.Activity;

/// <summary>A type whose work produces activity events, which <see cref="ActivityFeed"/> merges for the UI.</summary>
public interface IActivitySource
{
    /// <summary>The events this source originates: hot, never terminating, and reporting a failure as an event.</summary>
    IObservable<ActivityEvent> ActivityEvents { get; }
}
