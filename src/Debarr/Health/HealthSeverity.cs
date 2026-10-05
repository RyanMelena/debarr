namespace Debarr.Health;

/// <summary>How much a health message needs the operator, in increasing order.</summary>
public enum HealthSeverity
{
    /// <summary>Playback still sends a ratio, but something needs a look.</summary>
    Warning,

    /// <summary>Debarr can't do part of its work until the operator fixes it.</summary>
    Error,
}
