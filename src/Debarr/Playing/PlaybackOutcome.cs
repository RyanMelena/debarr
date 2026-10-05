using System.Text.Json.Serialization;

namespace Debarr.Playing;

/// <summary>What a playback sent, or why it sent nothing.</summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(Sent), "sent")]
[JsonDerivedType(typeof(Stream), "stream")]
[JsonDerivedType(typeof(DontSend), "dont_send")]
[JsonDerivedType(typeof(NoPlayerAspectRatio), "no_player_aspect_ratio")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record PlaybackOutcome
{
    private PlaybackOutcome()
    {
    }

    /// <param name="AspectRatio">The ratio sent: the override's as the operator set it, and every other ratio snapped.</param>
    /// <param name="DetectionId">The detection whose result was sent; null for any other source.</param>
    public sealed record Sent(double AspectRatio, NotificationAspectRatioSource Source, Guid? DetectionId) : PlaybackOutcome;

    /// <summary>A <c>plugin://</c> or <c>pvr://</c> stream.</summary>
    public sealed record Stream : PlaybackOutcome;

    /// <summary>The video file's override says don't send.</summary>
    public sealed record DontSend : PlaybackOutcome;

    /// <summary>The player ratio would be sent, and the player reported none.</summary>
    public sealed record NoPlayerAspectRatio : PlaybackOutcome;

    /// <summary>Handling the playback failed.</summary>
    /// <param name="Error">Why handling failed.</param>
    public sealed record Failed(string Error) : PlaybackOutcome;
}
