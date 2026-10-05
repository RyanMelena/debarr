using FluentResults;
using Wolverine.Persistence.EventSourcing;

namespace Debarr.Detecting;

/// <param name="AspectRatio">The ratio to send as the operator set it; null to send the current result or the player's ratio.</param>
public sealed record SaveOverride(FileHash VideoFile, double? AspectRatio, bool DontSend, string? Note)
{
    /// <summary>The video file's stream, which Wolverine loads the aggregate from.</summary>
    public Guid VideoFileId => VideoFile.StreamId;
}

public static class SaveOverrideHandler
{
    /// <summary>Refuses a video file that no longer exists or is archived, and a ratio of 0 or less beneath its field.</summary>
    public static Result<Override> Validate(SaveOverride command, VideoFile? videoFile) => videoFile switch
    {
        null => Result.Fail("The video file no longer exists."),
        { Archived: true } => Result.Fail("The video file is archived."),
        _ => Override.Create(command.AspectRatio, command.DontSend, command.Note, default),
    };

    public static OverrideSaved Handle(SaveOverride command, [WriteModel(Required = false)] VideoFile? videoFile, Override @override, TimeProvider timeProvider) =>
        new(@override with { SavedAt = timeProvider.GetUtcNow() });
}
