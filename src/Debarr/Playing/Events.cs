using Debarr.Detecting;
using Debarr.Scanning;

namespace Debarr.Playing;

public sealed record PlayerAdded(Guid PlayerId, string Name, bool Enabled, PlayerEndpoint Endpoint, IReadOnlyList<PathMapping> PathMappings);

public sealed record PlayerChanged(Guid PlayerId, bool Enabled, PlayerEndpoint Endpoint, IReadOnlyList<PathMapping> PathMappings);

public sealed record PlayerRenamed(Guid PlayerId, string Name);

public sealed record PlayerRemoved(Guid PlayerId);

/// <param name="PlaybackId">The playback's stream, which keys its History document.</param>
public sealed record PlaybackHandled(
    Guid PlaybackId,
    DateTimeOffset OccurredAt,
    Guid PlayerId,
    string PlayerName,
    string? Title,
    PlayerPath PlayerPath,
    LocalPath? LocalPath,
    FileHash? VideoFile,
    AspectRatio? PlayerAspectRatio,
    PlaybackOutcome Outcome);

/// <param name="PlaybackId">The playback's stream, which each notifier delivery document records.</param>
public sealed record DeliveryFinished(Guid PlaybackId, Delivery Delivery);

/// <param name="ClearedAt">History shows only the playbacks that occurred after it.</param>
public sealed record HistoryCleared(DateTimeOffset ClearedAt);
