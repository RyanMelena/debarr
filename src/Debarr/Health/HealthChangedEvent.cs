using Debarr.Activity;

namespace Debarr.Health;

/// <summary>The health messages changed, such as when a check finds a problem or one goes away.</summary>
public sealed record HealthChangedEvent : ActivityEvent;
