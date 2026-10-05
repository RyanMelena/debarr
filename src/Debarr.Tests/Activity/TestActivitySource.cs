using Debarr.Activity;

namespace Debarr.Tests.Activity;

/// <summary>An activity source whose events are the stream a test supplies.</summary>
public sealed class TestActivitySource(IObservable<ActivityEvent> activityEvents) : IActivitySource
{
    public IObservable<ActivityEvent> ActivityEvents => activityEvents;
}
