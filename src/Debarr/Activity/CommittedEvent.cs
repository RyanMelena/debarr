using JasperFx.Events;

namespace Debarr.Activity;

/// <summary>An event a commit appended, published once the commit ends.</summary>
public sealed record CommittedEvent(IEvent Event) : ActivityEvent;
