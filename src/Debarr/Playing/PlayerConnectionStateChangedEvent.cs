namespace Debarr.Playing;

/// <summary>A player connection moved to a new state, such as connected or disconnected.</summary>
public sealed record PlayerConnectionStateChangedEvent(Guid PlayerId, string PlayerName, PlayerConnectionState State)
    : PlayerConnectionEvent(PlayerId, PlayerName);
