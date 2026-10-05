using Debarr.Detecting;

namespace Debarr.Playing;

/// <summary>A player's report that it started playing a file, in terms every player type shares.</summary>
/// <param name="PlayerAspectRatio">The display aspect ratio the player reports; null when it reports none.</param>
/// <param name="Title">The title the player reports; null when it reports none.</param>
/// <param name="OccurredAt">When Debarr received the player's report.</param>
public sealed record PlaybackStartedEvent(
    Guid PlayerId,
    string PlayerName,
    PlayerPath PlayerPath,
    AspectRatio? PlayerAspectRatio,
    string? Title,
    DateTimeOffset OccurredAt) : PlayerConnectionEvent(PlayerId, PlayerName);
