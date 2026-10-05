using Debarr.Detecting;
using Debarr.Scanning;
using Wolverine.Fisher;

namespace Debarr.Playing;

/// <summary>Records a playback started event once it is handled, with what it sent or why it sent nothing.</summary>
/// <param name="PlaybackId">The new playback's stream.</param>
public sealed record RecordPlayback(
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

public static class RecordPlaybackHandler
{
    public static StartStream<Playback> Handle(RecordPlayback command) =>
        FisherOps.StartStream<Playback>(
            command.PlaybackId,
            new PlaybackHandled(
                command.PlaybackId,
                command.OccurredAt,
                command.PlayerId,
                command.PlayerName,
                command.Title,
                command.PlayerPath,
                command.LocalPath,
                command.VideoFile,
                command.PlayerAspectRatio,
                command.Outcome));
}
