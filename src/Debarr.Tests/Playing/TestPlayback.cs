using Debarr.Detecting;
using Debarr.Playing;
using Debarr.Scanning;
using Fisher;

namespace Debarr.Tests.Playing;

/// <summary>Playbacks and deliveries for tests, recorded as the app records them.</summary>
public static class TestPlayback
{
    /// <summary>Player ids whose text sorts Theater first.</summary>
    public static readonly Guid TheaterId = new("00000000-0000-0000-0000-000000000001");

    public static readonly Guid BedroomId = new("00000000-0000-0000-0000-000000000002");

    /// <summary>Notifier ids whose text sorts automation first.</summary>
    public static readonly Guid AutomationId = new("00000000-0000-0000-0000-00000000000a");

    public static readonly Guid LightsId = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A handled playback; a stream sent nothing, and any other path sent a detected 2.40.</summary>
    public static PlaybackHandled Handled(
        string playerName = "Theater",
        string? title = null,
        string playerPath = "/media/film.mkv",
        string? localPath = null,
        FileHash? videoFile = null,
        PlaybackOutcome? outcome = null,
        DateTimeOffset? occurredAt = null,
        Guid? playerId = null) =>
        new(
            Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            playerId ?? TheaterId,
            playerName,
            title,
            new PlayerPath(playerPath),
            localPath is null ? null : new LocalPath(localPath),
            videoFile,
            null,
            outcome ?? (new PlayerPath(playerPath).IsStream()
                ? new PlaybackOutcome.Stream()
                : new PlaybackOutcome.Sent(2.4, NotificationAspectRatioSource.Detected, null)));

    public static Delivery Delivery(Guid notifierId, string notifierName, DeliveryOutcome outcome, DateTimeOffset? startedAt = null) =>
        new(Guid.NewGuid(), notifierId, notifierName, startedAt ?? DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(12), outcome);

    /// <summary>Records each playback in one commit.</summary>
    public static async Task RecordAsync(IDocumentStore store, IEnumerable<PlaybackHandled> playbacks, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        foreach (var playback in playbacks)
        {
            session.Events.StartStream<Playback>(playback.PlaybackId, playback);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records the playback with its deliveries in one commit.</summary>
    public static async Task RecordAsync(IDocumentStore store, PlaybackHandled playback, IEnumerable<Delivery> deliveries, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        session.Events.StartStream<Playback>(
            playback.PlaybackId,
            [playback, .. deliveries.Select(delivery => new DeliveryFinished(playback.PlaybackId, delivery))]);
        await session.SaveChangesAsync(cancellationToken);
    }
}
