using Debarr.Activity;

namespace Debarr.Playing;

/// <summary>An event a player connection emits about its player.</summary>
public abstract record PlayerConnectionEvent(Guid PlayerId, string PlayerName) : ActivityEvent;
