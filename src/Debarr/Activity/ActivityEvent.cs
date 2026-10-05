namespace Debarr.Activity;

/// <summary>
/// Something Debarr did or is doing, published as it happens by the <see cref="IActivitySource"/> that did it.
/// The UI follows these through <see cref="ActivityFeed"/> to reload pages and show activity messages.
/// </summary>
public abstract record ActivityEvent;
