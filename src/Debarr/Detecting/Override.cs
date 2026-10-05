using FluentResults;

namespace Debarr.Detecting;

/// <summary>An optional fixed ratio, Don't Send flag and note on a video file, with when it was last saved.</summary>
/// <param name="AspectRatio">The ratio to send as the operator set it; null to send the current result or the player's ratio.</param>
/// <param name="Note">Null for no note, and otherwise trimmed.</param>
public sealed record Override(AspectRatio? AspectRatio, bool DontSend, string? Note, DateTimeOffset SavedAt)
{
    /// <summary>The override, with a blank note as none, or a field error for a ratio of 0 or less.</summary>
    public static Result<Override> Create(double? aspectRatio, bool dontSend, string? note, DateTimeOffset savedAt)
    {
        if (aspectRatio is { } value && !Detecting.AspectRatio.IsValid(value))
        {
            return Result.Fail(new FieldError(nameof(AspectRatio), "The ratio must be greater than 0."));
        }

        return new Override(
            aspectRatio is { } ratio ? new AspectRatio(ratio) : null,
            dontSend,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            savedAt);
    }

    /// <summary>Whether the override sets the video file's status to Manual: it has a ratio or Don't Send.</summary>
    public bool IsManual => AspectRatio is not null || DontSend;
}
