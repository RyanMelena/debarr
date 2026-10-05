using Debarr.Detecting;
using Debarr.Scanning;

namespace Debarr.Playing;

/// <summary>
/// The record of one playback started event: the player's name as it was, the title and paths, the video file,
/// the player-reported ratio, what it sent or why it sent nothing, and its deliveries.
/// </summary>
/// <param name="OccurredAt">When Debarr received the event; the notification's occurred_at.</param>
/// <param name="LocalPath">The translated path; null for a stream, or when handling failed before translating it.</param>
/// <param name="VideoFile">Null when the path is outside the library.</param>
public sealed record Playback(
    Guid Id,
    DateTimeOffset OccurredAt,
    string PlayerName,
    string? Title,
    PlayerPath PlayerPath,
    LocalPath? LocalPath,
    FileHash? VideoFile,
    AspectRatio? PlayerAspectRatio,
    PlaybackOutcome Outcome,
    IReadOnlyList<Delivery> Deliveries)
{
    public static Playback Create(PlaybackHandled handled) => new(
        handled.PlaybackId,
        handled.OccurredAt,
        handled.PlayerName,
        handled.Title,
        handled.PlayerPath,
        handled.LocalPath,
        handled.VideoFile,
        handled.PlayerAspectRatio,
        handled.Outcome,
        []);

    public Playback Apply(DeliveryFinished finished) => this with { Deliveries = [.. Deliveries, finished.Delivery] };

    /// <summary>The notification a playback sends; null when it sends nothing.</summary>
    public static Notification? NotificationFor(string playerName, DateTimeOffset occurredAt, PlaybackOutcome outcome) =>
        outcome is PlaybackOutcome.Sent sent
            ? new Notification { Player = playerName, OccurredAt = occurredAt, AspectRatio = sent.AspectRatio, Source = sent.Source }
            : null;
}

/// <summary>One attempt to send a playback's notification through one notifier, and how it ended.</summary>
/// <param name="NotifierName">The notifier's name when the attempt ran.</param>
public sealed record Delivery(Guid Id, Guid NotifierId, string NotifierName, DateTimeOffset StartedAt, TimeSpan Duration, DeliveryOutcome Outcome);
